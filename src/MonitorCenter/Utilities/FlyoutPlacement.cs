namespace MonitorCenter;

internal enum TaskbarEdge
{
    Left,
    Top,
    Right,
    Bottom
}

internal readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

internal readonly record struct PixelPoint(int X, int Y);

internal readonly record struct PixelSize(int Width, int Height);

internal static class FlyoutPlacement
{
    private const int Margin = 8;

    public static PixelPoint Calculate(
        PixelRect screen,
        PixelRect workArea,
        PixelPoint cursor,
        PixelSize flyout)
    {
        var edge = DetectTaskbarEdge(screen, workArea);

        var x = edge switch
        {
            TaskbarEdge.Left => workArea.Left + Margin,
            TaskbarEdge.Right => workArea.Right - flyout.Width - Margin,
            _ => cursor.X - flyout.Width / 2
        };

        var y = edge switch
        {
            TaskbarEdge.Top => workArea.Top + Margin,
            TaskbarEdge.Bottom => workArea.Bottom - flyout.Height - Margin,
            _ => cursor.Y - flyout.Height / 2
        };

        x = Clamp(x, workArea.Left + Margin, workArea.Right - flyout.Width - Margin);
        y = Clamp(y, workArea.Top + Margin, workArea.Bottom - flyout.Height - Margin);
        return new PixelPoint(x, y);
    }

    internal static TaskbarEdge DetectTaskbarEdge(PixelRect screen, PixelRect workArea)
    {
        if (workArea.Left > screen.Left)
        {
            return TaskbarEdge.Left;
        }

        if (workArea.Top > screen.Top)
        {
            return TaskbarEdge.Top;
        }

        if (workArea.Right < screen.Right)
        {
            return TaskbarEdge.Right;
        }

        return TaskbarEdge.Bottom;
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return maximum < minimum ? minimum : Math.Clamp(value, minimum, maximum);
    }
}
