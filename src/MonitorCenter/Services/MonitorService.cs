using System.Runtime.InteropServices;
using MonitorCenter.Interop;
using MonitorCenter.Models;

namespace MonitorCenter.Services;

internal sealed class MonitorService : IAsyncDisposable
{
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    // Held while displays are probed: by discovery, under the operation gate, and by a recheck, outside it. It keeps
    // the two from talking to the same display at once.
    private readonly SemaphoreSlim _probeGate = new(1, 1);
    private readonly WmiMonitorReader _wmiReader = new();
    private Dictionary<string, MonitorEndpoint> _endpoints = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, string> _displayNames = new(StringComparer.OrdinalIgnoreCase);

    // The DDC/CI command each display last answered, kept when it stops answering so it is asked that first again.
    private readonly Dictionary<string, BrightnessBackendKind> _lastDdcBackends = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    /// <param name="reprobeKnownDisplays">
    /// When false, displays that were already discovered keep their WMI identity and DDC/CI backend instead of
    /// being queried again; their brightness is still read fresh.
    /// </param>
    public async Task<IReadOnlyList<MonitorSnapshot>> DiscoverAsync(
        bool reprobeKnownDisplays = true,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            IReadOnlyDictionary<string, MonitorEndpoint> known = reprobeKnownDisplays
                ? new Dictionary<string, MonitorEndpoint>()
                : _endpoints;
            return await DiscoverUnderGateAsync(known, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>
    /// Asks the displays that did not answer DDC/CI again, on the handles they already have, leaving working
    /// displays alone. Runs a full discovery instead when one of them has no handle to ask.
    /// </summary>
    /// <remarks>
    /// A display that doesn't answer takes about a second of retries to probe, so the probing runs outside the
    /// operation gate and brightness changes on the displays that work aren't held up by it.
    /// </remarks>
    public async Task<IReadOnlyList<MonitorSnapshot>> RecheckUnavailableAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        (MonitorEndpoint Endpoint, BrightnessBackendKind KnownBackend)[] unavailable;
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var pending = _endpoints.Values
                .Where(endpoint => endpoint.Status != MonitorSupportStatus.Ready)
                .ToArray();
            if (pending.Any(endpoint => !endpoint.CanReconnect))
            {
                return await DiscoverUnderGateAsync(_endpoints, cancellationToken).ConfigureAwait(false);
            }

            unavailable = pending.Select(endpoint => (endpoint, LastDdcBackend(endpoint.Id))).ToArray();
        }
        finally
        {
            _operationGate.Release();
        }

        // A discovery that runs in the meantime replaces and disposes these endpoints; probing a disposed one
        // does nothing, and the snapshots published below come from whatever endpoints are current by then.
        await _probeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(() =>
            {
                foreach (var (endpoint, knownBackend) in unavailable)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    endpoint.TryReconnect(knownBackend);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _probeGate.Release();
        }

        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return PublishSnapshots();
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<IReadOnlyList<MonitorSnapshot>> DiscoverUnderGateAsync(
        IReadOnlyDictionary<string, MonitorEndpoint> known,
        CancellationToken cancellationToken)
    {
        await _probeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var discovered = await Task.Run(() => DiscoverCore(known), cancellationToken).ConfigureAwait(false);
            var oldEndpoints = _endpoints;
            _endpoints = discovered;
            var snapshots = PublishSnapshots();

            foreach (var endpoint in oldEndpoints.Values)
            {
                endpoint.Dispose();
            }

            return snapshots;
        }
        finally
        {
            _probeGate.Release();
        }
    }

    private IReadOnlyList<MonitorSnapshot> PublishSnapshots()
    {
        foreach (var endpoint in _endpoints.Values)
        {
            if (endpoint.Status == MonitorSupportStatus.Ready &&
                endpoint.Backend is BrightnessBackendKind.DdcHighLevel or BrightnessBackendKind.DdcVcp)
            {
                _lastDdcBackends[endpoint.Id] = endpoint.Backend;
            }
        }

        var snapshots = MonitorLabeler.OrderAndLabel(_endpoints.Values.Select(endpoint => endpoint.ToSnapshot()));
        _displayNames = snapshots.ToDictionary(
            snapshot => snapshot.Id,
            snapshot => snapshot.DisplayName,
            StringComparer.OrdinalIgnoreCase);
        return snapshots;
    }

    private BrightnessBackendKind LastDdcBackend(string id) =>
        _lastDdcBackends.TryGetValue(id, out var backend) ? backend : BrightnessBackendKind.None;

    public async Task<BrightnessResult> SetBrightnessAsync(
        string monitorId,
        int percent,
        BrightnessWriteMode mode = BrightnessWriteMode.Commit,
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
                    () => endpoint.SetBrightness(requested, mode),
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

    private Dictionary<string, MonitorEndpoint> DiscoverCore(IReadOnlyDictionary<string, MonitorEndpoint> known)
    {
        // WMI queries are comparatively expensive, so they run only when a display actually needs them.
        var metadata = new Lazy<IReadOnlyDictionary<string, WmiMonitorMetadata>>(_wmiReader.ReadMetadata);
        var wmiBrightness = new Lazy<IReadOnlyDictionary<string, WmiBrightnessState>>(_wmiReader.ReadBrightnessStates);
        var result = new Dictionary<string, MonitorEndpoint>(StringComparer.OrdinalIgnoreCase);
        Exception? callbackFailure = null;

        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            try
            {
                DiscoverLogicalMonitor(monitor, known, metadata, wmiBrightness, result);
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
        IReadOnlyDictionary<string, MonitorEndpoint> known,
        Lazy<IReadOnlyDictionary<string, WmiMonitorMetadata>> metadata,
        Lazy<IReadOnlyDictionary<string, WmiBrightnessState>> wmiBrightness,
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
        var stableBaseId = string.IsNullOrWhiteSpace(normalizedInstance)
            ? info.DeviceName
            : normalizedInstance;

        string friendlyName;
        string? serial;
        if (known.TryGetValue(stableBaseId, out var knownDisplay) ||
            known.TryGetValue($"{stableBaseId}:0", out knownDisplay))
        {
            friendlyName = knownDisplay.FriendlyName;
            serial = knownDisplay.Serial;
        }
        else
        {
            metadata.Value.TryGetValue(normalizedInstance, out var monitorMetadata);
            serial = monitorMetadata?.Serial;
            friendlyName = monitorMetadata?.FriendlyName ?? string.Empty;
            if (string.IsNullOrWhiteSpace(friendlyName) ||
                string.Equals(friendlyName, "Display", StringComparison.OrdinalIgnoreCase))
            {
                friendlyName = !string.IsNullOrWhiteSpace(displayDevice.DeviceString) &&
                               !displayDevice.DeviceString.Contains("Generic PnP", StringComparison.OrdinalIgnoreCase)
                    ? displayDevice.DeviceString
                    : info.DeviceName;
            }
        }

        WmiBrightnessState? ReadWmiState() =>
            wmiBrightness.Value.TryGetValue(normalizedInstance, out var state) ? state : null;

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
                serial,
                info.DeviceName,
                bounds,
                ReadWmiState());
            return;
        }

        var physicalMonitors = new NativeMethods.PhysicalMonitor[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(logicalMonitor, count, physicalMonitors))
        {
            AddWmiOrUnavailable(
                result,
                stableBaseId,
                friendlyName,
                serial,
                info.DeviceName,
                bounds,
                ReadWmiState());
            return;
        }

        for (var index = 0; index < physicalMonitors.Length; index++)
        {
            var physical = physicalMonitors[index];
            var handle = new SafePhysicalMonitorHandle(physical.Handle);
            var id = physicalMonitors.Length == 1 ? stableBaseId : $"{stableBaseId}:{index}";
            var knownBackend = known.TryGetValue(id, out var knownEndpoint) &&
                               knownEndpoint.Backend != BrightnessBackendKind.None
                ? knownEndpoint.Backend
                : LastDdcBackend(id);

            try
            {
                // A built-in panel already known to use WMI skips the DDC/CI probe it cannot answer.
                if (knownBackend == BrightnessBackendKind.WmiInternal && ReadWmiState() is { } knownWmiState)
                {
                    handle.Dispose();
                    result[id] = MonitorEndpoint.CreateWmi(
                        id,
                        friendlyName,
                        serial,
                        info.DeviceName,
                        bounds,
                        _wmiReader,
                        knownWmiState);
                    continue;
                }

                var endpoint = MonitorEndpoint.CreateDdc(
                    id,
                    friendlyName,
                    serial,
                    info.DeviceName,
                    bounds,
                    handle,
                    knownBackend);

                if (endpoint.Status != MonitorSupportStatus.Ready && ReadWmiState() is { } wmiState)
                {
                    endpoint.Dispose();
                    endpoint = MonitorEndpoint.CreateWmi(
                        id,
                        friendlyName,
                        serial,
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
        await _probeGate.WaitAsync().ConfigureAwait(false);

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
            _probeGate.Release();
            _probeGate.Dispose();
            _operationGate.Release();
            _operationGate.Dispose();
        }
    }
}
