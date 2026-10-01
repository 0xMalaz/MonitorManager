using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace MonitorCenter.UI;

internal sealed class MonitorDragAdorner : Adorner
{
    private const double PreviewSize = 84;
    private readonly Border _preview;
    private readonly VisualCollection _visuals;
    private WpfPoint _position;

    public MonitorDragAdorner(UIElement adornedElement, ImageSource snapshot)
        : base(adornedElement)
    {
        _visuals = new VisualCollection(this);
        IsHitTestVisible = false;
        _preview = new Border
        {
            Width = PreviewSize,
            Height = PreviewSize,
            CornerRadius = new CornerRadius(9),
            Background = new ImageBrush(snapshot) { Stretch = Stretch.Fill },
            BorderBrush = System.Windows.Application.Current.TryFindResource("AccentBrush") as System.Windows.Media.Brush
                          ?? System.Windows.Media.Brushes.DeepSkyBlue,
            BorderThickness = new Thickness(1),
            Opacity = 0.9,
            Effect = new DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 6,
                Opacity = 0.65,
                Color = Colors.Black
            }
        };
        _visuals.Add(_preview);
    }

    public void UpdatePosition(WpfPoint position)
    {
        var half = PreviewSize / 2;
        _position = new WpfPoint(
            Math.Clamp(position.X, half, Math.Max(half, AdornedElement.RenderSize.Width - half)),
            Math.Clamp(position.Y, half, Math.Max(half, AdornedElement.RenderSize.Height - half)));
        InvalidateArrange();
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    protected override WpfSize MeasureOverride(WpfSize constraint)
    {
        _preview.Measure(new WpfSize(PreviewSize, PreviewSize));
        return constraint;
    }

    protected override WpfSize ArrangeOverride(WpfSize finalSize)
    {
        _preview.Arrange(new Rect(
            _position.X - PreviewSize / 2,
            _position.Y - PreviewSize / 2,
            PreviewSize,
            PreviewSize));
        return finalSize;
    }
}
