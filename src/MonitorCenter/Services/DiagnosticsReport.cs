using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
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
    private const uint DisplayDeviceActive = 0x1;

    /// <summary>The gap after a DDC/CI read before the next one, so the next read starts on a quiet bus.</summary>
    private static readonly TimeSpan DdcPause = TimeSpan.FromMilliseconds(300);

    /// <summary>Must run with exclusive access to the displays; see <see cref="MonitorService.RunExclusiveAsync{T}"/>.</summary>
    public static string Collect(string appVersion, IReadOnlyList<MonitorSnapshot> appView)
    {
        var report = new StringBuilder();
        Section(report, "System", () => WriteSystem(report, appVersion));
        Section(report, "Displays as MonitorCenter sees them", () => WriteAppView(report, appView));
        Section(report, "Desktop areas Windows reports (MonitorCenter makes one tile for each)",
            () => WriteDesktopAreas(report));
        Section(report, "Graphics outputs and the displays attached to them", () => WriteDisplayDevices(report));
        Section(report, "WMI: WmiMonitorID", () => WriteWmi(report,
            "SELECT InstanceName, Active, ManufacturerName, ProductCodeID, UserFriendlyName, SerialNumberID FROM WmiMonitorID",
            item => $"maker={DecodeCharacters(item["ManufacturerName"])} " +
                    $"product={DecodeCharacters(item["ProductCodeID"])} " +
                    $"name=\"{DecodeCharacters(item["UserFriendlyName"])}\" " +
                    $"serial={MaskSerial(DecodeCharacters(item["SerialNumberID"]))}"));
        Section(report, "WMI: WmiMonitorConnectionParams (5 HDMI, 10 DisplayPort, 11 internal DisplayPort, 0x80000000 internal)",
            () => WriteWmi(report,
                "SELECT InstanceName, Active, VideoOutputTechnology FROM WmiMonitorConnectionParams",
                item => $"connection={FormatConnection(item["VideoOutputTechnology"])}"));
        Section(report, "WMI: WmiMonitorBrightness (built-in displays)", () => WriteWmi(report,
            "SELECT InstanceName, Active, CurrentBrightness, Level FROM WmiMonitorBrightness",
            item => $"current={item["CurrentBrightness"]} levels={(item["Level"] as Array)?.Length ?? 0}"));
        Section(report, "WMI: WmiMonitorBrightnessMethods", () => WriteWmi(report,
            "SELECT InstanceName, Active FROM WmiMonitorBrightnessMethods",
            _ => string.Empty));
        return report.ToString();
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
                $"\"{snapshot.DisplayName}\" device={snapshot.DisplayDevice} id={snapshot.Id} " +
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

    private static void Section(StringBuilder report, string title, Action write)
    {
        report.AppendLine().AppendLine($"== {title}");
        try
        {
            write();
        }
        catch (Exception exception)
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
                report.AppendLine($"Graphics: {gpu["Name"]}, driver {gpu["DriverVersion"]}, {gpu["PNPDeviceID"]}");
            }
        }
    }

    private static void WriteDesktopAreas(StringBuilder report)
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

        // The same ID MonitorService gives each area; areas sharing one are merged into a single tile.
        var ids = areas.ToDictionary(
            area => area.Monitor,
            area => AppId(area.Info.DeviceName));
        var shared = FindSharedIds(ids.Values);

        foreach (var (monitor, info) in areas)
        {
            var bounds = info.Monitor;
            var id = ids[monitor];
            report.AppendLine(
                $"{info.DeviceName} bounds=({bounds.Left},{bounds.Top})-({bounds.Right},{bounds.Bottom}) " +
                $"primary={(info.Flags & 1) != 0}");
            report.AppendLine($"    app id={id}" + (shared.Contains(id) ? "  ** shared with another desktop area **" : string.Empty));
            WritePhysicalMonitors(report, monitor);
        }
    }

    private static string AppId(string logicalDeviceName)
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

                var active = (display.StateFlags & DisplayDeviceActive) != 0 ? "active" : "not active";
                report.AppendLine(
                    $"    display {displayIndex}: \"{display.DeviceString}\" flags=0x{display.StateFlags:X} ({active}) " +
                    display.DeviceId);
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
                    report.AppendLine($"{item["InstanceName"]} active={item["Active"]} {describe(item)}".TrimEnd());
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
        var scope = new ManagementScope(scopePath);
        scope.Connect();
        using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(query));
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
