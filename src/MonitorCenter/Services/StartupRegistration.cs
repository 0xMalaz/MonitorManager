using System.IO;
using Microsoft.Win32;

namespace MonitorCenter.Services;

internal sealed class StartupRegistration
{
    internal const string ValueName = "MonitorCenter";
    internal const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string PreferencesKeyPath = @"Software\MonitorCenter";
    private const string StartupConfiguredValue = "StartupConfigured";

    private readonly string _executablePath;

    public StartupRegistration(string executablePath)
    {
        _executablePath = Path.GetFullPath(executablePath);
    }

    public bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            return key?.GetValue(ValueName) is string value &&
                   string.Equals(value, BuildCommand(_executablePath), StringComparison.OrdinalIgnoreCase);
        }
    }

    public void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);

        if (enabled)
        {
            key.SetValue(ValueName, BuildCommand(_executablePath), RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }

        using var preferences = Registry.CurrentUser.CreateSubKey(PreferencesKeyPath, writable: true);
        preferences.SetValue(StartupConfiguredValue, 1, RegistryValueKind.DWord);
    }

    public void EnsureDefaultEnabled()
    {
        using var preferences = Registry.CurrentUser.CreateSubKey(PreferencesKeyPath, writable: true);
        var hasPreference = preferences.GetValue(StartupConfiguredValue) is not null;

        using var runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
        var hasExistingRegistration = runKey?.GetValue(ValueName) is string;

        if (!hasPreference || hasExistingRegistration)
        {
            SetEnabled(true);
        }
    }

    internal static string BuildCommand(string executablePath)
    {
        return $"\"{Path.GetFullPath(executablePath)}\" --startup";
    }
}
