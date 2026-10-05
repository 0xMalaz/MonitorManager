namespace MonitorCenter.Models;

internal sealed record BrightnessResult(int RequestedPercent, int ActualPercent, MonitorSnapshot Snapshot);

internal enum BrightnessWriteMode
{
    /// <summary>Write the value and read it back, retrying until the display confirms it.</summary>
    Commit,

    /// <summary>Write the value once without reading it back; used while the user is still adjusting.</summary>
    Preview,

    /// <summary>Read the display back and write the value again only when it does not match.</summary>
    Verify
}
