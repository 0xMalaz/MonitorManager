using MonitorCenter.Models;

namespace MonitorCenter.Services;

/// <summary>A brightness reading a display returned over DDC/CI, and the command that read it.</summary>
internal readonly record struct DdcBrightnessReading(
    BrightnessBackendKind Backend,
    uint Minimum,
    uint Current,
    uint Maximum);

/// <summary>
/// Finds which DDC/CI brightness command a display answers. Many displays drop or garble a share of their DDC/CI
/// replies, so one failed read does not mean the display cannot be controlled: every attempt tries both commands,
/// and a failed attempt is repeated after a short pause.
/// </summary>
internal static class DdcBrightnessProbe
{
    internal static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(250),
        TimeSpan.FromMilliseconds(500)
    ];

    /// <param name="readHighLevel">Reads brightness with GetMonitorBrightness; null when the call fails.</param>
    /// <param name="readVcp">Reads MCCS VCP 0x10; null when the call fails.</param>
    /// <param name="knownBackend">The command this display answered before, which is tried first.</param>
    /// <param name="sleep">Waits between attempts.</param>
    /// <returns>The first valid reading, or null when every attempt failed.</returns>
    public static DdcBrightnessReading? Probe(
        Func<DdcBrightnessReading?> readHighLevel,
        Func<DdcBrightnessReading?> readVcp,
        BrightnessBackendKind knownBackend,
        Action<TimeSpan> sleep)
    {
        // A display already known to answer only MCCS VCP 0x10 is asked that first.
        var vcpFirst = knownBackend == BrightnessBackendKind.DdcVcp;

        for (var attempt = 0; ; attempt++)
        {
            var reading = vcpFirst
                ? Valid(readVcp()) ?? Valid(readHighLevel())
                : Valid(readHighLevel()) ?? Valid(readVcp());

            if (reading is not null || attempt == RetryDelays.Length)
            {
                return reading;
            }

            sleep(RetryDelays[attempt]);
        }
    }

    private static DdcBrightnessReading? Valid(DdcBrightnessReading? reading) =>
        reading is { } value && value.Maximum > value.Minimum ? value : null;
}
