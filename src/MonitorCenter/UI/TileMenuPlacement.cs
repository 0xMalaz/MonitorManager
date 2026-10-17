using System.Windows;
using System.Windows.Controls.Primitives;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace MonitorCenter.UI;

/// <summary>Places a tile's options menu just under its options button, with their right edges aligned.</summary>
public static class TileMenuPlacement
{
    private const double Gap = 3;

    /// <summary>Transparent room around the menu for its shadow, which placement leaves out.</summary>
    public static Thickness ShadowMargin { get; } = new(10, 6, 10, 14);

    internal static CustomPopupPlacementCallback Create(FrameworkElement button) => (popupSize, targetSize, _) =>
    {
        // The sizes can arrive in device pixels rather than layout units; the button's own size gives the ratio.
        var scale = button.ActualWidth > 0 ? targetSize.Width / button.ActualWidth : 1;
        return Calculate(popupSize, targetSize, scale);
    };

    internal static CustomPopupPlacement[] Calculate(WpfSize popupSize, WpfSize targetSize, double scale)
    {
        var x = targetSize.Width - popupSize.Width + ShadowMargin.Right * scale;
        var below = new WpfPoint(x, targetSize.Height + (Gap - ShadowMargin.Top) * scale);
        var above = new WpfPoint(x, -popupSize.Height + (ShadowMargin.Bottom - Gap) * scale);
        return
        [
            new CustomPopupPlacement(below, PopupPrimaryAxis.Horizontal),
            new CustomPopupPlacement(above, PopupPrimaryAxis.Horizontal)
        ];
    }
}
