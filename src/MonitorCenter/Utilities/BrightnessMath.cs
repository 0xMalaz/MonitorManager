namespace MonitorCenter;

internal static class BrightnessMath
{
    public static int ToPercent(uint current, uint minimum, uint maximum)
    {
        if (maximum <= minimum)
        {
            return 0;
        }

        var bounded = Math.Clamp(current, minimum, maximum);
        var percentage = (bounded - minimum) * 100d / (maximum - minimum);
        return (int)Math.Round(percentage, MidpointRounding.AwayFromZero);
    }

    public static uint FromPercent(int percent, uint minimum, uint maximum)
    {
        if (maximum <= minimum)
        {
            return minimum;
        }

        var bounded = Math.Clamp(percent, 0, 100);
        var raw = minimum + (maximum - minimum) * bounded / 100d;
        return (uint)Math.Round(raw, MidpointRounding.AwayFromZero);
    }
}
