using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MonitorCenter.Interop;
using MonitorCenter.Models;

namespace MonitorCenter.Services;

/// <summary>
/// Builds the plain-text report sent with "Report a problem": what Windows says about the displays, what each display
/// answers over DDC/CI, and what MonitorCenter made of them.
/// </summary>
/// <remarks>
/// Only reads. It never changes brightness or any setting, and serial numbers are replaced by a short hash so the
/// report can't identify a particular display. Each section catches its own failures so one failing query doesn't
/// lose the rest of the report.
/// </remarks>
internal static class DiagnosticsReport
{
    /// <summary>A device instance Windows made up: three hex numbers and a location, joined by '&amp;'.</summary>
    private static readonly Regex WindowsInstance = new(@"^[0-9A-F]+&[0-9A-F]+&[0-9A-F]+&[0-9A-Z_]+$", RegexOptions.IgnoreCase);

    /// <summary>The gap after a DDC/CI read before the next one, so the next read starts on a quiet bus.</summary>
    private static readonly TimeSpan DdcPause = TimeSpan.FromMilliseconds(300);

    /// <summary>The longest a single WMI query may take to return its results.</summary>
    private static readonly TimeSpan WmiTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Collects the whole report; cancelling it returns at once and releases the displays.</summary>
    public static Task<string> CollectAsync(
        string appVersion,
        MonitorService monitorService,
        CancellationToken cancellationToken) =>
        CollectAsync(
            token => monitorService.RunExclusiveAsync(snapshots => CollectDisplays(snapshots, token), token),
            token => CollectFromWindows(appVersion, token),
            cancellationToken);

    /// <remarks>
    /// The display reads need the displays to themselves. The WMI queries don't, and run apart from them: Windows'
    /// WMI service can take minutes to answer when it's unhealthy, and a call that has started can't be stopped, so
    /// when the report is cancelled they are left to finish in the background instead of being waited for.
    /// </remarks>
    internal static async Task<string> CollectAsync(
        Func<CancellationToken, Task<string>> collectDisplays,
        Func<CancellationToken, (string System, string Wmi)> collectFromWindows,
        CancellationToken cancellationToken)
    {
        var fromWindows = Task.Run(() => collectFromWindows(cancellationToken), cancellationToken);
        var displays = await collectDisplays(cancellationToken).ConfigureAwait(false);
        var (system, wmi) = await fromWindows.WaitAsync(cancellationToken).ConfigureAwait(false);
        return system + displays + wmi;
    }

    /// <summary>The sections read from the displays; must run with exclusive access to them.</summary>
    /// <remarks><paramref name="cancellationToken"/> is checked between sections and between displays.</remarks>
    internal static string CollectDisplays(IReadOnlyList<MonitorSnapshot> appView, CancellationToken cancellationToken)
    {
        var report = new StringBuilder();
        Add(report, "Displays as MonitorCenter sees them", () => WriteAppView(report, appView), cancellationToken);
        Add(report, "Desktop areas Windows reports (MonitorCenter makes one tile for each)",
            () => WriteDesktopAreas(report, cancellationToken), cancellationToken);
        Add(report, "Graphics outputs and the displays attached to them", () => WriteDisplayDevices(report), cancellationToken);
        return report.ToString();
    }

    private static (string System, string Wmi) CollectFromWindows(string appVersion, CancellationToken cancellationToken)
    {
        var system = new StringBuilder();
        Add(system, "System", () => WriteSystem(system, appVersion), cancellationToken);

        var report = new StringBuilder();
        void AddWmi(string title, Action write) => Add(report, title, write, cancellationToken);
        AddWmi("WMI: WmiMonitorID", () => WriteWmi(report,
            "SELECT InstanceName, Active, ManufacturerName, ProductCodeID, UserFriendlyName, SerialNumberID FROM WmiMonitorID",
            item => $"maker={DecodeCharacters(item["ManufacturerName"])} " +
                    $"product={DecodeCharacters(item["ProductCodeID"])} " +
                    $"name=\"{DecodeCharacters(item["UserFriendlyName"])}\" " +
                    $"serial={MaskSerial(DecodeCharacters(item["SerialNumberID"]))}"));
        AddWmi("WMI: WmiMonitorConnectionParams (5 HDMI, 10 DisplayPort, 11 internal DisplayPort, 0x80000000 internal)",
            () => WriteWmi(report,
                "SELECT InstanceName, Active, VideoOutputTechnology FROM WmiMonitorConnectionParams",
                item => $"connection={FormatConnection(item["VideoOutputTechnology"])}"));
        AddWmi("WMI: WmiMonitorBrightness (built-in displays)", () => WriteWmi(report,
            "SELECT InstanceName, Active, CurrentBrightness, Level FROM WmiMonitorBrightness",
            item => $"current={item["CurrentBrightness"]} levels={(item["Level"] as Array)?.Length ?? 0}"));
        AddWmi("WMI: WmiMonitorBrightnessMethods", () => WriteWmi(report,
            "SELECT InstanceName, Active FROM WmiMonitorBrightnessMethods",
            _ => string.Empty));
        return (system.ToString(), report.ToString());
    }

    /// <summary>
    /// Masks the part of a device ID that can hold a serial number. A device instance ID is enumerator, hardware ID
    /// and instance; Windows makes the instance up (for example 5&amp;31321161&amp;0&amp;UID4353), unless the device
    /// reports a unique serial number, which is then used as the instance. Only instances in Windows' own format are
    /// kept. Interface paths (\\?\DISPLAY#...#{guid}) are the same ID
    /// with '#' between the parts.
    /// </summary>
    internal static string MaskDeviceId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return "-";
        }

        var prefix = id.StartsWith(@"\\?\", StringComparison.Ordinal) || id.StartsWith(@"\\.\", StringComparison.Ordinal)
            ? id[..4]
            : string.Empty;
        var separator = prefix.Length > 0 && id.Contains('#') ? '#' : '\\';
        var parts = id[prefix.Length..].Split(separator);
        if (parts.Length < 3 || WindowsInstance.IsMatch(parts[2]) || parts[2].StartsWith('{'))
        {
            return id;
        }

        parts[2] = MaskSerial(parts[2]);
        return prefix + string.Join(separator, parts);
    }

    /// <summary>Replaces a serial number with a short hash: the same display still matches across reports.</summary>
    internal static string MaskSerial(string? serial)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            return "-";
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(serial.Trim()));
        return "#" + Convert.ToHexString(hash, 0, 4);
    }

    internal static void WriteAppView(StringBuilder report, IReadOnlyList<MonitorSnapshot> appView)
    {
        if (appView.Count == 0)
        {
            report.AppendLine("(no displays)");
            return;
        }

        foreach (var snapshot in appView)
        {
            report.AppendLine(
                $"\"{snapshot.DisplayName}\" device={snapshot.DisplayDevice} id={MaskDeviceId(snapshot.Id)} " +
                $"serial={MaskSerial(snapshot.Serial)}");
            report.AppendLine(
                $"    status={snapshot.Status} control={snapshot.Backend} " +
                $"raw={snapshot.MinimumRaw}/{snapshot.CurrentRaw}/{snapshot.MaximumRaw} (min/current/max)" +
                (snapshot.ErrorMessage is { } error ? $" error=\"{error}\"" : string.Empty));
        }
    }

    /// <summary>Returns the IDs that more than one desktop area would get, which hides all but one of them.</summary>
    internal static IReadOnlySet<string> FindSharedIds(IEnumerable<string> ids) =>
        ids.GroupBy(id => id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static void Add(StringBuilder report, string title, Action write, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        report.AppendLine().AppendLine($"== {title}");
        try
        {
            write();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            report.AppendLine($"(failed: {exception.GetType().Name}: {exception.Message})");
        }
    }

    private static void WriteSystem(StringBuilder report, string appVersion)
    {
        report.AppendLine($"MonitorCenter {appVersion}");
        report.AppendLine($"Windows {Environment.OSVersion.Version}, {RuntimeInformation.OSArchitecture}");
        report.AppendLine($"Language {CultureInfo.CurrentUICulture.Name}, generated {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");

        foreach (var computer in Query(@"\\.\root\cimv2", "SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
        {
            using (computer)
            {
                report.AppendLine($"Computer: {computer["Manufacturer"]} {computer["Model"]}");
            }
        }

        foreach (var gpu in Query(@"\\.\root\cimv2", "SELECT Name, DriverVersion, PNPDeviceID FROM Win32_VideoController"))
        {
            using (gpu)
            {
                report.AppendLine($"Graphics: {gpu["Name"]}, driver {gpu["DriverVersion"]}, {MaskDeviceId(gpu["PNPDeviceID"] as string)}");
            }
        }
    }

    private static void WriteDesktopAreas(StringBuilder report, CancellationToken cancellationToken)
    {
        var areas = new List<(IntPtr Monitor, NativeMethods.MonitorInfoEx Info)>();
        NativeMethods.MonitorEnumProc callback = (monitor, _, _, _) =>
        {
            var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                areas.Add((monitor, info));
            }
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
        GC.KeepAlive(callback);

        // The display MonitorService takes each area to show. Before 1.11.1, areas showing the same one were merged
        // into a single tile; now the later ones get their device name added to the ID.
        var ids = areas.ToDictionary(
            area => area.Monitor,
            area => ShownDisplayId(area.Info.DeviceName));
        var shared = FindSharedIds(ids.Values);

        foreach (var (monitor, info) in areas)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bounds = info.Monitor;
            var id = ids[monitor];
            report.AppendLine(
                $"{info.DeviceName} bounds=({bounds.Left},{bounds.Top})-({bounds.Right},{bounds.Bottom}) " +
                $"primary={(info.Flags & 1) != 0}");
            report.AppendLine($"    shows display={MaskDeviceId(id)}" + (shared.Contains(id) ? "  ** also taken as the display of another desktop area **" : string.Empty));
            WritePhysicalMonitors(report, monitor);
        }
    }

    private static string ShownDisplayId(string logicalDeviceName)
    {
        var normalized = WmiMonitorReader.NormalizeInstanceName(MonitorService.ReadDisplayDevice(logicalDeviceName).DeviceId);
        return string.IsNullOrWhiteSpace(normalized) ? logicalDeviceName : normalized;
    }

    private static void WritePhysicalMonitors(StringBuilder report, IntPtr monitor)
    {
        if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(monitor, out var count))
        {
            report.AppendLine($"    physical monitors: failed, {LastError()}");
            return;
        }

        report.AppendLine($"    physical monitors: {count}");
        if (count == 0)
        {
            return;
        }

        var physicalMonitors = new NativeMethods.PhysicalMonitor[count];
        if (!NativeMethods.GetPhysicalMonitorsFromHMONITOR(monitor, count, physicalMonitors))
        {
            report.AppendLine($"    physical monitors: failed, {LastError()}");
            return;
        }

        for (var index = 0; index < physicalMonitors.Length; index++)
        {
            using var handle = new SafePhysicalMonitorHandle(physicalMonitors[index].Handle);
            report.AppendLine($"    [{index}] \"{physicalMonitors[index].Description}\"");

            // VCP 0x10 first on a quiet bus, then the two reads MonitorCenter makes back to back when probing.
            report.AppendLine($"        VCP 0x10 alone:        {ReadVcp(handle)}");
            Thread.Sleep(DdcPause);
            report.AppendLine($"        GetMonitorBrightness:  {ReadHighLevel(handle)}");
            report.AppendLine($"        VCP 0x10 right after:  {ReadVcp(handle)}");
            Thread.Sleep(DdcPause);
        }
    }

    private static string ReadHighLevel(SafePhysicalMonitorHandle handle)
    {
        var stopwatch = Stopwatch.StartNew();
        return NativeMethods.GetMonitorBrightness(handle, out var minimum, out var current, out var maximum)
            ? $"ok min={minimum} current={current} max={maximum} ({stopwatch.ElapsedMilliseconds} ms)"
            : $"failed {LastError()} ({stopwatch.ElapsedMilliseconds} ms)";
    }

    private static string ReadVcp(SafePhysicalMonitorHandle handle)
    {
        var stopwatch = Stopwatch.StartNew();
        return NativeMethods.GetVCPFeatureAndVCPFeatureReply(
            handle, NativeMethods.VCP_BRIGHTNESS, IntPtr.Zero, out var current, out var maximum)
            ? $"ok current={current} max={maximum} ({stopwatch.ElapsedMilliseconds} ms)"
            : $"failed {LastError()} ({stopwatch.ElapsedMilliseconds} ms)";
    }

    private static void WriteDisplayDevices(StringBuilder report)
    {
        report.AppendLine("(output flags: 0x1 on the desktop, 0x4 primary; display flags: 0x1 active, 0x2 attached)");
        for (uint adapterIndex = 0; ; adapterIndex++)
        {
            var adapter = NewDisplayDevice();
            if (!NativeMethods.EnumDisplayDevices(null, adapterIndex, ref adapter, 0))
            {
                break;
            }

            report.AppendLine($"{adapter.DeviceName} \"{adapter.DeviceString}\" flags=0x{adapter.StateFlags:X}");
            for (uint displayIndex = 0; ; displayIndex++)
            {
                var display = NewDisplayDevice();
                if (!NativeMethods.EnumDisplayDevices(
                        adapter.DeviceName, displayIndex, ref display, NativeMethods.EDD_GET_DEVICE_INTERFACE_NAME))
                {
                    break;
                }

                var active = (display.StateFlags & NativeMethods.DISPLAY_DEVICE_ACTIVE) != 0 ? "active" : "not active";
                report.AppendLine(
                    $"    display {displayIndex}: \"{display.DeviceString}\" flags=0x{display.StateFlags:X} ({active}) " +
                    MaskDeviceId(display.DeviceId));
            }
        }
    }

    private static NativeMethods.DisplayDevice NewDisplayDevice() =>
        new() { Size = Marshal.SizeOf<NativeMethods.DisplayDevice>() };

    private static void WriteWmi(StringBuilder report, string query, Func<ManagementBaseObject, string> describe)
    {
        var any = false;
        try
        {
            foreach (var item in Query(@"\\.\root\wmi", query))
            {
                using (item)
                {
                    any = true;
                    report.AppendLine($"{MaskDeviceId(item["InstanceName"] as string)} active={item["Active"]} {describe(item)}".TrimEnd());
                }
            }
        }
        catch (ManagementException exception) when (exception.ErrorCode == ManagementStatus.NotSupported)
        {
            // What Windows answers when no display offers this, for example the brightness classes on a desktop PC.
            report.AppendLine("(not supported by any display)");
            return;
        }

        if (!any)
        {
            report.AppendLine("(none)");
        }
    }

    private static IEnumerable<ManagementBaseObject> Query(string scopePath, string query)
    {
        var scope = new ManagementScope(scopePath, new ConnectionOptions { Timeout = WmiTimeout });
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(
            scope,
            new ObjectQuery(query),
            new EnumerationOptions { Timeout = WmiTimeout, ReturnImmediately = true, Rewindable = false });
        using var results = searcher.Get();
        foreach (var item in results)
        {
            yield return item;
        }
    }

    private static string FormatConnection(object? value) => value switch
    {
        uint technology => technology >= 0x80000000 ? $"0x{technology:X}" : technology.ToString(CultureInfo.InvariantCulture),
        int technology => technology < 0 ? $"0x{technology:X}" : technology.ToString(CultureInfo.InvariantCulture),
        null => "-",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "-"
    };

    private static string DecodeCharacters(object? value) => value switch
    {
        ushort[] characters => new string(characters.Where(c => c != 0).Select(c => (char)c).ToArray()).Trim(),
        byte[] characters => new string(characters.Where(c => c != 0).Select(c => (char)c).ToArray()).Trim(),
        _ => string.Empty
    };

    private static string LastError() => $"0x{Marshal.GetLastWin32Error():X8}";
}
