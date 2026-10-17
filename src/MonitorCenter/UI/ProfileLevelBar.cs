using System.Windows;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;

namespace MonitorCenter.UI;

/// <summary>A small bar on a profile tile showing the brightness the profile sets on one display.</summary>
public sealed class ProfileLevelBar : FrameworkElement
{
    private const double CornerRadius = 2;
    private const double MinimumFillHeight = 2;

    private static readonly DependencyProperty TrackProperty = RegisterBrush("Track");
    private static readonly DependencyProperty FillProperty = RegisterBrush("Fill");

    public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(
        nameof(Level),
        typeof(int?),
        typeof(ProfileLevelBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public ProfileLevelBar()
    {
        SnapsToDevicePixels = true;
        SetResourceReference(TrackProperty, "ProfileLevelTrackBrush");
        SetResourceReference(FillProperty, "ProfileLevelFillBrush");
    }

    /// <summary>The brightness in percent, or null when the profile leaves the display unchanged.</summary>
    public int? Level
    {
        get => (int?)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.PushClip(new RectangleGeometry(bounds, CornerRadius, CornerRadius));
        drawingContext.DrawRectangle(GetBrush(TrackProperty), null, bounds);

        if (Level is { } level)
        {
            // Even the lowest level keeps a sliver, so a saved value never looks like a missing one.
            var height = Math.Max(MinimumFillHeight, bounds.Height * Math.Clamp(level, 0, 100) / 100d);
            drawingContext.DrawRectangle(
                GetBrush(FillProperty), null, new Rect(0, bounds.Bottom - height, bounds.Width, height));
        }

        drawingContext.Pop();
    }

    private MediaBrush GetBrush(DependencyProperty property) =>
        GetValue(property) as MediaBrush ?? MediaBrushes.Transparent;

    private static DependencyProperty RegisterBrush(string name) =>
        DependencyProperty.Register(
            name,
            typeof(MediaBrush),
            typeof(ProfileLevelBar),
            new FrameworkPropertyMetadata(MediaBrushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
}
