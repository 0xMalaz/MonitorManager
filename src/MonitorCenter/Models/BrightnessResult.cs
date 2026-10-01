namespace MonitorCenter.Models;

internal sealed record BrightnessResult(int RequestedPercent, int ActualPercent, MonitorSnapshot Snapshot);
