namespace MonitorCenter.Models;

internal sealed class AppSettings
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public List<BrightnessProfile> Profiles { get; set; } = [];
    public List<DisplayNamePreference> DisplayNames { get; set; } = [];
}

internal sealed class MonitorIdentity
{
    public string Id { get; set; } = string.Empty;
    public string? Serial { get; set; }
    public string FriendlyName { get; set; } = string.Empty;
}

internal sealed class BrightnessProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Profile";
    public List<ProfileMonitorSetting> MonitorSettings { get; set; } = [];
}

internal sealed class ProfileMonitorSetting
{
    public MonitorIdentity Identity { get; set; } = new();
    public int? Brightness { get; set; }
}

internal sealed class DisplayNamePreference
{
    public MonitorIdentity Identity { get; set; } = new();
    public string Name { get; set; } = string.Empty;
}
