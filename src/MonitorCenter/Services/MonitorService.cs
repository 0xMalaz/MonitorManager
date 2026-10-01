using System.Runtime.InteropServices;
using MonitorCenter.Interop;
using MonitorCenter.Models;

namespace MonitorCenter.Services;

internal sealed class MonitorService : IAsyncDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private readonly WmiMonitorReader _wmiReader = new();
    private Dictionary<string, MonitorEndpoint> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public async Task<IReadOnlyList<MonitorSnapshot>> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var discovered = await Task.Run(DiscoverCore, cancellationToken).ConfigureAwait(false);
            var snapshots = MonitorLabeler.OrderAndLabel(discovered.Values.Select(endpoint => endpoint.ToSnapshot()));
            var oldEndpoints = _endpoints;
            _endpoints = discovered;
            _displayNames = snapshots.ToDictionary(
                snapshot => snapshot.Id,
                snapshot => snapshot.DisplayName,
                StringComparer.OrdinalIgnoreCase);

            foreach (var endpoint in oldEndpoints.Values)
            {
                endpoint.Dispose();
            }

            return snapshots;
        }
        finally
        {
            _operationGate.Release();
        }
    }

    public async Task<BrightnessResult> SetBrightnessAsync(
        string monitorId,
        int percent,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (!_endpoints.TryGetValue(monitorId, out var endpoint))
            {
                throw new InvalidOperationException("The display is no longer connected.");
            }

            var requested = Math.Clamp(percent, 0, 100);
            var actual = await Task.Run(
                    () => endpoint.SetBrightnessAndRead(requested),
                    cancellationToken)
                .ConfigureAwait(false);
            _displayNames.TryGetValue(monitorId, out var displayName);
            return new BrightnessResult(requested, actual, endpoint.ToSnapshot(displayName));
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private Dictionary<string, MonitorEndpoint> DiscoverCore()
    {
        var metadata = _wmiReader.ReadMetadata();
        var wmiBrightness = _wmiReader.ReadBrightnessStates();
        var result = new Dictionary<string, MonitorEndpoint>(StringComparer.OrdinalIgnoreCase);
        Exception? callbackFailure = null;

        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            try
            {
                DiscoverLogicalMonitor(monitor, metadata, wmiBrightness, result);
                return true;
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                return false;
            }
        };

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            foreach (var endpoint in result.Values)
            {
                endpoint.Dispose();
            }

            throw callbackFailure ?? NativeMethods.LastError("Windows could not enumerate displays");
        }

        return result;
    }

    private void DiscoverLogicalMonitor(
        IntPtr logicalMonitor,
        IReadOnlyDictionary<string, WmiMonitorMetadata> metadata,
        IReadOnlyDictionary<string, WmiBrightnessState> wmiBrightness,
        IDictionary<string, MonitorEndpoint> result)
    {
        var info = new NativeMethods.MonitorInfoEx
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>()
        };

        if (!NativeMethods.GetMonitorInfo(logicalMonitor, ref info))
        {
            throw NativeMethods.LastError("Windows could not read display information");
        }

        var displayDevice = ReadDisplayDevice(info.DeviceName);
        var normalizedInstance = WmiMonitorReader.NormalizeInstanceName(displayDevice.DeviceId);
        metadata.TryGetValue(normalizedInstance, out var monitorMetadata);
        wmiBrightness.TryGetValue(normalizedInstance, out var wmiState);

        var friendlyName = monitorMetadata?.FriendlyName;
        if (string.IsNullOrWhiteSpace(friendlyName) ||
            string.Equals(friendlyName, "Display", StringComparison.OrdinalIgnoreCase))
        {
            friendlyName = !string.IsNullOrWhiteSpace(displayDevice.DeviceString) &&
                           !displayDevice.DeviceString.Contains("Generic PnP", StringComparison.OrdinalIgnoreCase)
                ? displayDevice.DeviceString
                : info.DeviceName;
        }

        var stableBaseId = string.IsNullOrWhiteSpace(normalizedInstance)
            ? info.DeviceName
            : normalizedInstance;
        var bounds = new DesktopBounds(
            info.Monitor.Left,
            info.Monitor.Top,
            info.Monitor.Right,
            info.Monitor.Bottom);

        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(logicalMonitor, out var count) || count == 0)
        {
            AddWmiOrUnavailable(
                result,
                stableBaseId,
                friendlyName,
                monitorMetadata?.Serial,
                info.DeviceName,
                bounds,
                wmiState);
            return;
        }

        var physicalMonitors = new NativeMethods.PhysicalMonitor[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(logicalMonitor, count, physicalMonitors))
        {
            AddWmiOrUnavailable(
                result,
                stableBaseId,
                friendlyName,
                monitorMetadata?.Serial,
                info.DeviceName,
                bounds,
                wmiState);
            return;
        }

        for (var index = 0; index < physicalMonitors.Length; index++)
        {
            var physical = physicalMonitors[index];
            var handle = new SafePhysicalMonitorHandle(physical.Handle);
            var id = physicalMonitors.Length == 1 ? stableBaseId : $"{stableBaseId}:{index}";

            try
            {
                var endpoint = MonitorEndpoint.CreateDdc(
                    id,
                    friendlyName,
                    monitorMetadata?.Serial,
                    info.DeviceName,
                    bounds,
                    handle);

                if (endpoint.Status != MonitorSupportStatus.Ready && wmiState is not null)
                {
                    endpoint.Dispose();
                    endpoint = MonitorEndpoint.CreateWmi(
                        id,
                        friendlyName,
                        monitorMetadata?.Serial,
                        info.DeviceName,
                        bounds,
                        _wmiReader,
                        wmiState);
                }

                result[id] = endpoint;
            }
            catch
            {
                handle.Dispose();

                for (var remaining = index + 1; remaining < physicalMonitors.Length; remaining++)
                {
                    if (physicalMonitors[remaining].Handle != IntPtr.Zero)
                    {
                        NativeMethods.DestroyPhysicalMonitor(physicalMonitors[remaining].Handle);
                    }
                }

                throw;
            }
        }
    }

    private void AddWmiOrUnavailable(
        IDictionary<string, MonitorEndpoint> result,
        string id,
        string friendlyName,
        string? serial,
        string displayDevice,
        DesktopBounds bounds,
        WmiBrightnessState? wmiState)
    {
        result[id] = wmiState is not null
            ? MonitorEndpoint.CreateWmi(id, friendlyName, serial, displayDevice, bounds, _wmiReader, wmiState)
            : MonitorEndpoint.CreateUnavailable(id, friendlyName, serial, displayDevice, bounds);
    }

    private static NativeMethods.DisplayDevice ReadDisplayDevice(string logicalDeviceName)
    {
        var device = new NativeMethods.DisplayDevice
        {
            Size = Marshal.SizeOf<NativeMethods.DisplayDevice>()
        };

        if (!NativeMethods.EnumDisplayDevices(
                logicalDeviceName,
                0,
                ref device,
                NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
        {
            device.DeviceName = logicalDeviceName;
            device.DeviceString = logicalDeviceName;
            device.DeviceId = logicalDeviceName;
        }

        return device;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _operationGate.WaitAsync().ConfigureAwait(false);

        try
        {
            foreach (var endpoint in _endpoints.Values)
            {
                endpoint.Dispose();
            }

            _endpoints.Clear();
        }
        finally
        {
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }
}
