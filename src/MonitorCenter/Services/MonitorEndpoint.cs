using System.ComponentModel;
using System.Runtime.InteropServices;
using MonitorCenter.Interop;
using MonitorCenter.Models;

namespace MonitorCenter.Services;

internal sealed class MonitorEndpoint : IDisposable
{
    private const string UnavailableMessage = "Brightness unavailable — check DDC/CI and the display cable.";

    private readonly SafePhysicalMonitorHandle? _physicalHandle;
    private readonly WmiMonitorReader? _wmiReader;
    private readonly string? _wmiInstanceName;
    private bool _disposed;

    private MonitorEndpoint(
        string id,
        string friendlyName,
        string? serial,
        string displayDevice,
        DesktopBounds bounds,
        SafePhysicalMonitorHandle? physicalHandle,
        WmiMonitorReader? wmiReader,
        string? wmiInstanceName,
        uint minimum,
        uint maximum,
        uint current,
        BrightnessBackendKind backend,
        MonitorSupportStatus status,
        string? errorMessage)
    {
        Id = id;
        FriendlyName = friendlyName;
        Serial = serial;
        DisplayDevice = displayDevice;
        Bounds = bounds;
        _physicalHandle = physicalHandle;
        _wmiReader = wmiReader;
        _wmiInstanceName = wmiInstanceName;
        MinimumRaw = minimum;
        MaximumRaw = maximum;
        CurrentRaw = current;
        Backend = backend;
        Status = status;
        ErrorMessage = errorMessage;
    }

    public string Id { get; }
    public string FriendlyName { get; }
    public string? Serial { get; }
    public string DisplayDevice { get; }
    public DesktopBounds Bounds { get; }
    public uint MinimumRaw { get; private set; }
    public uint MaximumRaw { get; private set; }
    public uint CurrentRaw { get; private set; }
    public BrightnessBackendKind Backend { get; private set; }
    public MonitorSupportStatus Status { get; private set; }
    public string? ErrorMessage { get; private set; }

    public static MonitorEndpoint CreateDdc(
        string id,
        string friendlyName,
        string? serial,
        string displayDevice,
        DesktopBounds bounds,
        SafePhysicalMonitorHandle handle,
        BrightnessBackendKind knownBackend = BrightnessBackendKind.None)
    {
        // A display already known to answer only MCCS VCP 0x10 skips the high-level probe it rejects.
        if (knownBackend == BrightnessBackendKind.DdcVcp &&
            TryReadVcpBrightness(handle, out var vcpCurrent, out var vcpMaximum))
        {
            return CreateVcp(vcpCurrent, vcpMaximum);
        }

        if (NativeMethods.GetMonitorBrightness(handle, out var minimum, out var current, out var maximum) &&
            maximum > minimum)
        {
            return new MonitorEndpoint(
                id,
                friendlyName,
                serial,
                displayDevice,
                bounds,
                handle,
                null,
                null,
                minimum,
                maximum,
                current,
                BrightnessBackendKind.DdcHighLevel,
                MonitorSupportStatus.Ready,
                null);
        }

        if (knownBackend != BrightnessBackendKind.DdcVcp &&
            TryReadVcpBrightness(handle, out current, out maximum))
        {
            return CreateVcp(current, maximum);
        }

        return new MonitorEndpoint(
            id,
            friendlyName,
            serial,
            displayDevice,
            bounds,
            handle,
            null,
            null,
            0,
            100,
            0,
            BrightnessBackendKind.None,
            MonitorSupportStatus.Unavailable,
            UnavailableMessage);

        MonitorEndpoint CreateVcp(uint vcpCurrent, uint vcpMaximum) => new(
            id,
            friendlyName,
            serial,
            displayDevice,
            bounds,
            handle,
            null,
            null,
            0,
            vcpMaximum,
            vcpCurrent,
            BrightnessBackendKind.DdcVcp,
            MonitorSupportStatus.Ready,
            null);
    }

    private static bool TryReadVcpBrightness(SafePhysicalMonitorHandle handle, out uint current, out uint maximum) =>
        NativeMethods.GetVCPFeatureAndVCPFeatureReply(
            handle,
            NativeMethods.VCP_BRIGHTNESS,
            IntPtr.Zero,
            out current,
            out maximum) && maximum > 0;

    public static MonitorEndpoint CreateWmi(
        string id,
        string friendlyName,
        string? serial,
        string displayDevice,
        DesktopBounds bounds,
        WmiMonitorReader wmiReader,
        WmiBrightnessState state)
    {
        return new MonitorEndpoint(
            id,
            friendlyName,
            serial,
            displayDevice,
            bounds,
            null,
            wmiReader,
            state.InstanceName,
            0,
            100,
            state.CurrentBrightness,
            BrightnessBackendKind.WmiInternal,
            MonitorSupportStatus.Ready,
            null);
    }

    public static MonitorEndpoint CreateUnavailable(
        string id,
        string friendlyName,
        string? serial,
        string displayDevice,
        DesktopBounds bounds,
        string? message = null)
    {
        return new MonitorEndpoint(
            id,
            friendlyName,
            serial,
            displayDevice,
            bounds,
            null,
            null,
            null,
            0,
            100,
            0,
            BrightnessBackendKind.None,
            MonitorSupportStatus.Unavailable,
            message ?? UnavailableMessage);
    }

    public MonitorSnapshot ToSnapshot(string? displayName = null)
    {
        return new MonitorSnapshot(
            Id,
            FriendlyName,
            displayName ?? FriendlyName,
            Serial,
            DisplayDevice,
            Bounds,
            MinimumRaw,
            MaximumRaw,
            CurrentRaw,
            Backend,
            Status,
            ErrorMessage);
    }

    public int SetBrightness(int percent, BrightnessWriteMode mode = BrightnessWriteMode.Commit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (Status != MonitorSupportStatus.Ready)
        {
            throw new InvalidOperationException(ErrorMessage ?? UnavailableMessage);
        }

        var requested = Math.Clamp(percent, 0, 100);

        int actualPercent;

        switch (Backend)
        {
            case BrightnessBackendKind.DdcHighLevel:
            case BrightnessBackendKind.DdcVcp:
                actualPercent = SetDdc(requested, mode);
                break;
            case BrightnessBackendKind.WmiInternal:
                actualPercent = SetWmi(requested, mode);
                break;
            default:
                throw new InvalidOperationException(UnavailableMessage);
        }

        ErrorMessage = null;
        return actualPercent;
    }

    private int SetDdc(int requestedPercent, BrightnessWriteMode mode)
    {
        try
        {
            switch (mode)
            {
                case BrightnessWriteMode.Preview:
                    WriteDdc(requestedPercent);
                    return BrightnessMath.ToPercent(CurrentRaw, MinimumRaw, MaximumRaw);
                case BrightnessWriteMode.Verify:
                    ReadCurrentDdc();
                    var actual = BrightnessMath.ToPercent(CurrentRaw, MinimumRaw, MaximumRaw);
                    if (Math.Abs(actual - requestedPercent) <= 1)
                    {
                        return actual;
                    }
                    break;
            }
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            // Fall back to a confirmed write, which retries transient DDC/CI failures.
        }

        return SetDdcAndReadWithRetry(requestedPercent);
    }

    private int SetWmi(int requestedPercent, BrightnessWriteMode mode)
    {
        var brightness = (byte)requestedPercent;
        switch (mode)
        {
            case BrightnessWriteMode.Preview:
                _wmiReader!.SetBrightness(_wmiInstanceName!, brightness);
                CurrentRaw = brightness;
                return BrightnessMath.ToPercent(CurrentRaw, MinimumRaw, MaximumRaw);
            case BrightnessWriteMode.Verify:
                if (_wmiReader!.ReadBrightness(_wmiInstanceName!) is byte current &&
                    Math.Abs(current - requestedPercent) <= 1)
                {
                    CurrentRaw = current;
                    return BrightnessMath.ToPercent(CurrentRaw, MinimumRaw, MaximumRaw);
                }
                break;
        }

        CurrentRaw = _wmiReader!.SetBrightnessAndRead(_wmiInstanceName!, brightness);
        return BrightnessMath.ToPercent(CurrentRaw, MinimumRaw, MaximumRaw);
    }

    private int SetDdcAndReadWithRetry(int requestedPercent)
    {
        Exception? lastFailure = null;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                WriteDdc(requestedPercent);
                ReadCurrentDdc();
                var actual = BrightnessMath.ToPercent(CurrentRaw, MinimumRaw, MaximumRaw);

                if (Math.Abs(actual - requestedPercent) <= 1)
                {
                    return actual;
                }

                lastFailure = new InvalidOperationException(
                    $"The display reported {actual}% after {requestedPercent}% was requested.");
            }
            catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
            {
                lastFailure = exception;
            }

            Thread.Sleep(80);
        }

        throw lastFailure ?? new InvalidOperationException("The display did not confirm its brightness change.");
    }

    private void WriteDdc(int percent)
    {
        if (Backend == BrightnessBackendKind.DdcVcp)
        {
            SetDdcVcp(percent);
        }
        else
        {
            SetDdcHighLevel(percent);
        }
    }

    private void SetDdcHighLevel(int percent)
    {
        var raw = BrightnessMath.FromPercent(percent, MinimumRaw, MaximumRaw);
        if (NativeMethods.SetMonitorBrightness(_physicalHandle!, raw))
        {
            CurrentRaw = raw;
            return;
        }

        // Some displays reject the high-level setter but implement MCCS VCP 0x10.
        if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                _physicalHandle!,
                NativeMethods.VCP_BRIGHTNESS,
                IntPtr.Zero,
                out _,
                out var maximum) && maximum > 0 &&
            NativeMethods.SetVCPFeature(
                _physicalHandle!,
                NativeMethods.VCP_BRIGHTNESS,
                BrightnessMath.FromPercent(percent, 0, maximum)))
        {
            Backend = BrightnessBackendKind.DdcVcp;
            MinimumRaw = 0;
            MaximumRaw = maximum;
            CurrentRaw = BrightnessMath.FromPercent(percent, 0, maximum);
            return;
        }

        throw NativeMethods.LastError("The monitor rejected the brightness command");
    }

    private void SetDdcVcp(int percent)
    {
        var raw = BrightnessMath.FromPercent(percent, MinimumRaw, MaximumRaw);
        if (!NativeMethods.SetVCPFeature(_physicalHandle!, NativeMethods.VCP_BRIGHTNESS, raw))
        {
            throw NativeMethods.LastError("The monitor rejected the DDC/CI brightness command");
        }

        CurrentRaw = raw;
    }

    private void ReadCurrentDdc()
    {
        if (Backend == BrightnessBackendKind.DdcVcp)
        {
            ReadDdcVcp();
        }
        else
        {
            ReadDdcHighLevel();
        }
    }

    private void ReadDdcHighLevel()
    {
        var lastError = 0;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (NativeMethods.GetMonitorBrightness(
                    _physicalHandle!,
                    out var minimum,
                    out var current,
                    out var maximum) && maximum > minimum)
            {
                MinimumRaw = minimum;
                MaximumRaw = maximum;
                CurrentRaw = current;
                return;
            }

            lastError = Marshal.GetLastWin32Error();
            Thread.Sleep(60);
        }

        if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                _physicalHandle!,
                NativeMethods.VCP_BRIGHTNESS,
                IntPtr.Zero,
                out var vcpCurrent,
                out var vcpMaximum) && vcpMaximum > 0)
        {
            Backend = BrightnessBackendKind.DdcVcp;
            MinimumRaw = 0;
            MaximumRaw = vcpMaximum;
            CurrentRaw = vcpCurrent;
            return;
        }

        throw new Win32Exception(lastError, "The monitor did not return its brightness");
    }

    private void ReadDdcVcp()
    {
        var lastError = 0;

        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(
                    _physicalHandle!,
                    NativeMethods.VCP_BRIGHTNESS,
                    IntPtr.Zero,
                    out var current,
                    out var maximum) && maximum > 0)
            {
                MinimumRaw = 0;
                MaximumRaw = maximum;
                CurrentRaw = current;
                return;
            }

            lastError = Marshal.GetLastWin32Error();
            Thread.Sleep(60);
        }

        throw new Win32Exception(lastError, "The monitor did not return its DDC/CI brightness");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _physicalHandle?.Dispose();
    }

}
