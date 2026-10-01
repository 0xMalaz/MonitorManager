namespace MonitorCenter.Models;

internal enum BrightnessBackendKind
{
    None,
    DdcHighLevel,
    DdcVcp,
    WmiInternal
}

internal enum MonitorSupportStatus
{
    Ready,
    Unavailable,
    Disconnected,
    Error
}

internal readonly record struct DesktopBounds(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

internal sealed record MonitorSnapshot(
    string Id,
    string FriendlyName,
    string DisplayName,
    string? Serial,
    string DisplayDevice,
    DesktopBounds Bounds,
    uint MinimumRaw,
    uint MaximumRaw,
    uint CurrentRaw,
    BrightnessBackendKind Backend,
    MonitorSupportStatus Status,
    string? ErrorMessage)
{
    public bool IsControllable => Status == MonitorSupportStatus.Ready && Backend != BrightnessBackendKind.None;

    public int CurrentPercent => BrightnessMath.ToPercent(CurrentRaw, MinimumRaw, MaximumRaw);
}
