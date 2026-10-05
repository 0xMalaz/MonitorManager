using System.Management;

namespace MonitorCenter.Services;

internal sealed record WmiMonitorMetadata(string InstanceName, string FriendlyName, string? Serial);

internal sealed record WmiBrightnessState(string InstanceName, byte CurrentBrightness);

internal sealed class WmiMonitorReader
{
    private const string ScopePath = @"\\.\root\wmi";

    public IReadOnlyDictionary<string, WmiMonitorMetadata> ReadMetadata()
    {
        var result = new Dictionary<string, WmiMonitorMetadata>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = CreateSearcher(
                "SELECT InstanceName, UserFriendlyName, SerialNumberID FROM WmiMonitorID WHERE Active = TRUE");
            using var collection = searcher.Get();

            foreach (ManagementObject monitor in collection)
            {
                using (monitor)
                {
                    var instanceName = monitor["InstanceName"] as string;
                    if (string.IsNullOrWhiteSpace(instanceName))
                    {
                        continue;
                    }

                    var friendlyName = DecodeCharacterArray(monitor["UserFriendlyName"]);
                    var serial = DecodeCharacterArray(monitor["SerialNumberID"]);
                    var normalized = NormalizeInstanceName(instanceName);

                    result[normalized] = new WmiMonitorMetadata(
                        instanceName,
                        string.IsNullOrWhiteSpace(friendlyName) ? "Display" : friendlyName,
                        string.IsNullOrWhiteSpace(serial) ? null : serial);
                }
            }
        }
        catch (ManagementException)
        {
            // DDC/CI discovery remains usable when WMI is unavailable.
        }
        catch (UnauthorizedAccessException)
        {
            // The app does not elevate solely to obtain friendly names.
        }

        return result;
    }

    public IReadOnlyDictionary<string, WmiBrightnessState> ReadBrightnessStates()
    {
        var result = new Dictionary<string, WmiBrightnessState>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var searcher = CreateSearcher(
                "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness WHERE Active = TRUE");
            using var collection = searcher.Get();

            foreach (ManagementObject monitor in collection)
            {
                using (monitor)
                {
                    var instanceName = monitor["InstanceName"] as string;
                    if (string.IsNullOrWhiteSpace(instanceName))
                    {
                        continue;
                    }

                    var current = Convert.ToByte(monitor["CurrentBrightness"]);
                    result[NormalizeInstanceName(instanceName)] = new WmiBrightnessState(instanceName, current);
                }
            }
        }
        catch (ManagementException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return result;
    }

    public byte SetBrightnessAndRead(string instanceName, byte brightness)
    {
        SetBrightness(instanceName, brightness);
        return ReadBrightness(instanceName) ?? brightness;
    }

    public byte? ReadBrightness(string instanceName) =>
        ReadBrightnessStates().TryGetValue(NormalizeInstanceName(instanceName), out var state)
            ? state.CurrentBrightness
            : null;

    public void SetBrightness(string instanceName, byte brightness)
    {
        var normalizedTarget = NormalizeInstanceName(instanceName);
        var methodFound = false;

        using (var searcher = CreateSearcher(
                   "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active = TRUE"))
        using (var collection = searcher.Get())
        {
            foreach (ManagementObject method in collection)
            {
                using (method)
                {
                    var candidate = method["InstanceName"] as string;
                    if (string.IsNullOrWhiteSpace(candidate) ||
                        !string.Equals(
                            NormalizeInstanceName(candidate),
                            normalizedTarget,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    methodFound = true;
                    using var input = method.GetMethodParameters("WmiSetBrightness");
                    input["Timeout"] = 1u;
                    input["Brightness"] = brightness;
                    using var output = method.InvokeMethod("WmiSetBrightness", input, null);
                    var returnValue = output?["ReturnValue"] is null
                        ? 0u
                        : Convert.ToUInt32(output["ReturnValue"]);

                    if (returnValue != 0)
                    {
                        throw new InvalidOperationException($"WMI brightness command failed with code {returnValue}.");
                    }

                    break;
                }
            }
        }

        if (!methodFound)
        {
            throw new InvalidOperationException("The built-in display is no longer available.");
        }
    }

    internal static string NormalizeInstanceName(string value)
    {
        var normalized = value.Trim();

        if (normalized.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            normalized = normalized[4..];
        }

        var guidMarker = normalized.IndexOf("#{", StringComparison.Ordinal);
        if (guidMarker >= 0)
        {
            normalized = normalized[..guidMarker];
        }

        normalized = normalized.Replace('#', '\\');

        if (normalized.EndsWith("_0", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[..^2];
        }

        return normalized.ToUpperInvariant();
    }

    private static ManagementObjectSearcher CreateSearcher(string query)
    {
        var scope = new ManagementScope(ScopePath);
        scope.Connect();
        return new ManagementObjectSearcher(scope, new ObjectQuery(query));
    }

    private static string DecodeCharacterArray(object? value)
    {
        var text = value switch
        {
            ushort[] characters => new string(characters
                .Where(character => character != 0)
                .Select(character => (char)character)
                .ToArray()),
            byte[] bytes => new string(bytes
                .Where(character => character != 0)
                .Select(character => (char)character)
                .ToArray()),
            _ => string.Empty
        };

        return text.Trim();
    }
}
