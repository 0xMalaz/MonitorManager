using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;
using WpfSize = System.Windows.Size;

namespace MonitorCenter.UI;

public sealed class BrightnessBox : FrameworkElement
{
    private static readonly DependencyProperty TileBackgroundProperty = RegisterBrush("TileBackground");
    private static readonly DependencyProperty TileHoverProperty = RegisterBrush("TileHover");
    private static readonly DependencyProperty TileDisabledProperty = RegisterBrush("TileDisabled");
    private static readonly DependencyProperty TileFillProperty = RegisterBrush("TileFill");
    private static readonly DependencyProperty TileDisabledFillProperty = RegisterBrush("TileDisabledFill");
    private static readonly DependencyProperty TileFillEdgeProperty = RegisterBrush("TileFillEdge");
    private static readonly DependencyProperty TileBorderProperty = RegisterBrush("TileBorder");
    private static readonly DependencyProperty TileStrongBorderProperty = RegisterBrush("TileStrongBorder");
    private static readonly DependencyProperty TileFocusBorderProperty = RegisterBrush("TileFocusBorder");
    private bool _isDragging;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(int),
        typeof(BrightnessBox),
        new FrameworkPropertyMetadata(
            0,
            FrameworkPropertyMetadataOptions.AffectsRender |
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            null,
            CoerceValue));

    public static readonly DependencyProperty IsInteractionEnabledProperty = DependencyProperty.Register(
        nameof(IsInteractionEnabled),
        typeof(bool),
        typeof(BrightnessBox),
        new FrameworkPropertyMetadata(true));

    public BrightnessBox()
    {
        Focusable = true;
        Cursor = System.Windows.Input.Cursors.SizeNS;
        SnapsToDevicePixels = true;
        IsEnabledChanged += (_, _) => InvalidateVisual();
        MouseEnter += (_, _) => InvalidateVisual();
        MouseLeave += (_, _) => InvalidateVisual();
        GotKeyboardFocus += (_, _) => InvalidateVisual();
        LostKeyboardFocus += (_, _) => InvalidateVisual();
        SetResourceReference(TileBackgroundProperty, "TileBackgroundBrush");
        SetResourceReference(TileHoverProperty, "TileHoverBrush");
        SetResourceReference(TileDisabledProperty, "TileDisabledBrush");
        SetResourceReference(TileFillProperty, "TileFillBrush");
        SetResourceReference(TileDisabledFillProperty, "TileDisabledFillBrush");
        SetResourceReference(TileFillEdgeProperty, "TileFillEdgeBrush");
        SetResourceReference(TileBorderProperty, "BorderBrush");
        SetResourceReference(TileStrongBorderProperty, "StrongBorderBrush");
        SetResourceReference(TileFocusBorderProperty, "AccentBrush");
    }

    public event EventHandler? ValueCommitRequested;

    public int Value
    {
        get => (int)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public bool IsInteractionEnabled
    {
        get => (bool)GetValue(IsInteractionEnabledProperty);
        set => SetValue(IsInteractionEnabledProperty, value);
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 300 : availableSize.Width;
        return new WpfSize(Math.Max(1, width), 96);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ActualWidth <= 1 || ActualHeight <= 1)
        {
            return;
        }

        const double cornerRadius = 9;
        var bounds = new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1);
        var clip = new RectangleGeometry(bounds, cornerRadius, cornerRadius);
        var background = !IsEnabled
            ? GetBrush(TileDisabledProperty)
            : IsMouseOver
                ? GetBrush(TileHoverProperty)
                : GetBrush(TileBackgroundProperty);
        var borderBrush = IsKeyboardFocused
            ? GetBrush(TileFocusBorderProperty)
            : IsEnabled && IsMouseOver
                ? GetBrush(TileStrongBorderProperty)
                : GetBrush(TileBorderProperty);
        var border = new MediaPen(borderBrush, IsKeyboardFocused ? 2 : 1);

        drawingContext.PushClip(clip);
        drawingContext.DrawRectangle(background, null, bounds);

        var fillHeight = bounds.Height * Value / 100d;
        if (fillHeight > 0)
        {
            var fill = new Rect(bounds.Left, bounds.Bottom - fillHeight, bounds.Width, fillHeight);
            drawingContext.DrawRectangle(
                IsEnabled ? GetBrush(TileFillProperty) : GetBrush(TileDisabledFillProperty),
                null,
                fill);

            if (IsEnabled && Value < 100)
            {
                drawingContext.DrawLine(
                    new MediaPen(GetBrush(TileFillEdgeProperty), 1),
                    new WpfPoint(bounds.Left, fill.Top + 0.5),
                    new WpfPoint(bounds.Right, fill.Top + 0.5));
            }
        }

        drawingContext.Pop();
        drawingContext.DrawRoundedRectangle(null, border, bounds, cornerRadius, cornerRadius);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);

        if (!IsEnabled || !IsInteractionEnabled)
        {
            return;
        }

        Focus();
        _isDragging = CaptureMouse();
        UpdateFromPointer(e.GetPosition(this).Y);
        e.Handled = true;
    }

    protected override void OnMouseMove(WpfMouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!_isDragging)
        {
            return;
        }

        if (e.LeftButton == MouseButtonState.Pressed)
        {
            UpdateFromPointer(e.GetPosition(this).Y);
        }
        else
        {
            CompleteInteraction();
        }

        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);

        if (!_isDragging)
        {
            return;
        }

        UpdateFromPointer(e.GetPosition(this).Y);
        CompleteInteraction();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(WpfMouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        if (_isDragging)
        {
            _isDragging = false;
            ValueCommitRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!IsEnabled || !IsInteractionEnabled)
        {
            return;
        }

        Value = Math.Clamp(Value + (e.Delta > 0 ? 2 : -2), 0, 100);
        ValueCommitRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    protected override void OnKeyDown(WpfKeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsEnabled || !IsInteractionEnabled)
        {
            return;
        }

        var next = e.Key switch
        {
            Key.Up or Key.Right => Value + 1,
            Key.Down or Key.Left => Value - 1,
            Key.PageUp => Value + 5,
            Key.PageDown => Value - 5,
            Key.Home => 0,
            Key.End => 100,
            _ => Value
        };

        if (next == Value && e.Key is not (Key.Home or Key.End))
        {
            return;
        }

        Value = Math.Clamp(next, 0, 100);
        ValueCommitRequested?.Invoke(this, EventArgs.Empty);
        e.Handled = true;
    }

    internal static int ValueFromY(double y, double height)
    {
        if (height <= 0 || double.IsNaN(height) || double.IsInfinity(height))
        {
            return 0;
        }

        var boundedY = Math.Clamp(y, 0, height);
        var percentage = (1d - boundedY / height) * 100d;
        return (int)Math.Round(percentage, MidpointRounding.AwayFromZero);
    }

    private void UpdateFromPointer(double y)
    {
        Value = ValueFromY(y, ActualHeight);
    }

    private void CompleteInteraction()
    {
        _isDragging = false;

        if (IsMouseCaptured)
        {
            ReleaseMouseCapture();
        }

        ValueCommitRequested?.Invoke(this, EventArgs.Empty);
    }

    private static object CoerceValue(DependencyObject dependencyObject, object baseValue)
    {
        return Math.Clamp((int)baseValue, 0, 100);
    }

    private MediaBrush GetBrush(DependencyProperty property) =>
        GetValue(property) as MediaBrush ?? MediaBrushes.Transparent;

    private static DependencyProperty RegisterBrush(string name) =>
        DependencyProperty.Register(
            name,
            typeof(MediaBrush),
            typeof(BrightnessBox),
            new FrameworkPropertyMetadata(MediaBrushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));
}
