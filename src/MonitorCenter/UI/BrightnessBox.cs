using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using MediaPen = System.Windows.Media.Pen;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfSize = System.Windows.Size;

namespace MonitorCenter.UI;

public sealed class BrightnessBox : FrameworkElement
{
    private static readonly DependencyProperty FrameProperty = RegisterBrush("Frame");
    private static readonly DependencyProperty FrameHoverProperty = RegisterBrush("FrameHover");
    private static readonly DependencyProperty BezelProperty = RegisterBrush("Bezel");
    private static readonly DependencyProperty ScreenProperty = RegisterBrush("Screen");
    private static readonly DependencyProperty FillTopProperty = RegisterBrush("FillTop");
    private static readonly DependencyProperty FillBottomProperty = RegisterBrush("FillBottom");
    private static readonly DependencyProperty DisabledFillProperty = RegisterBrush("DisabledFill");
    private static readonly DependencyProperty FocusBorderProperty = RegisterBrush("FocusBorder");

    /// <summary>The frame and bezel around the screen; only the screen maps to brightness.</summary>
    private const double ScreenInset = 5;
    private const double FrameCornerRadius = 6.5;
    private const double ScreenCornerRadius = 2.5;
    private LinearGradientBrush? _fillBrush;
    private bool _isDragging;
    private int _wheelDelta;

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value),
        typeof(int),
        typeof(BrightnessBox),
        new FrameworkPropertyMetadata(
            0,
            FrameworkPropertyMetadataOptions.AffectsRender |
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnValueChanged,
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
        MouseLeave += (_, _) => { _wheelDelta = 0; InvalidateVisual(); };
        GotKeyboardFocus += (_, _) => InvalidateVisual();
        LostKeyboardFocus += (_, _) => InvalidateVisual();
        SetResourceReference(FrameProperty, "MonitorFrameBrush");
        SetResourceReference(FrameHoverProperty, "MonitorFrameHoverBrush");
        SetResourceReference(BezelProperty, "MonitorBezelBrush");
        SetResourceReference(ScreenProperty, "MonitorScreenBrush");
        SetResourceReference(FillTopProperty, "MonitorFillTopBrush");
        SetResourceReference(FillBottomProperty, "MonitorFillBottomBrush");
        SetResourceReference(DisabledFillProperty, "MonitorDisabledFillBrush");
        SetResourceReference(FocusBorderProperty, "AccentBrush");
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
        var width = double.IsInfinity(availableSize.Width) ? 100 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 82 : availableSize.Height;
        return new WpfSize(Math.Max(1, width), Math.Max(1, height));
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (ActualWidth <= ScreenInset * 2 + 1 || ActualHeight <= ScreenInset * 2 + 1)
        {
            return;
        }

        var frame = new Rect(0.5, 0.5, ActualWidth - 1, ActualHeight - 1);
        var frameBrush = IsEnabled && IsMouseOver ? GetBrush(FrameHoverProperty) : GetBrush(FrameProperty);
        drawingContext.DrawRoundedRectangle(
            GetBrush(BezelProperty), new MediaPen(frameBrush, 1), frame, FrameCornerRadius, FrameCornerRadius);

        var screen = ScreenBounds(ActualWidth, ActualHeight);
        drawingContext.PushClip(new RectangleGeometry(screen, ScreenCornerRadius, ScreenCornerRadius));
        drawingContext.DrawRectangle(GetBrush(ScreenProperty), null, screen);

        var fillHeight = screen.Height * Value / 100d;
        if (fillHeight > 0)
        {
            var fill = new Rect(screen.Left, screen.Bottom - fillHeight, screen.Width, fillHeight);
            drawingContext.DrawRectangle(IsEnabled ? GetFillBrush() : GetBrush(DisabledFillProperty), null, fill);
        }

        drawingContext.Pop();
        if (IsKeyboardFocused)
        {
            var focus = new Rect(1, 1, ActualWidth - 2, ActualHeight - 2);
            drawingContext.DrawRoundedRectangle(
                null, new MediaPen(GetBrush(FocusBorderProperty), 2), focus, FrameCornerRadius, FrameCornerRadius);
        }
    }

    private static Rect ScreenBounds(double width, double height) =>
        new(ScreenInset, ScreenInset, width - ScreenInset * 2, height - ScreenInset * 2);

    /// <summary>The fill fades from light at its top edge to deep blue at the bottom, whatever its height.</summary>
    private MediaBrush GetFillBrush()
    {
        var top = (GetBrush(FillTopProperty) as SolidColorBrush)?.Color ?? Colors.Transparent;
        var bottom = (GetBrush(FillBottomProperty) as SolidColorBrush)?.Color ?? Colors.Transparent;
        if (_fillBrush is null || _fillBrush.GradientStops[0].Color != top || _fillBrush.GradientStops[1].Color != bottom)
        {
            _fillBrush = new LinearGradientBrush(top, bottom, 90);
            _fillBrush.Freeze();
        }

        return _fillBrush;
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

        var steps = ConsumeWheelDelta(ref _wheelDelta, e.Delta);
        if (steps != 0)
        {
            Value = (int)Math.Clamp(Value + (long)steps * 2, 0, 100);
            // Keep explicit commits even at 0/100: they may supersede an in-flight profile write.
            ValueCommitRequested?.Invoke(this, EventArgs.Empty);
        }
        e.Handled = true;
    }

    /// <summary>High-resolution wheels can split a notch across several events; only whole notches commit.</summary>
    internal static int ConsumeWheelDelta(ref int remainder, int delta)
    {
        if (delta == 0) return 0;
        if (Math.Sign(remainder) != Math.Sign(delta)) remainder = 0;
        var total = (long)remainder + delta;
        var steps = (int)(total / Mouse.MouseWheelDeltaForOneLine);
        remainder = (int)(total % Mouse.MouseWheelDeltaForOneLine);
        return steps;
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

    protected override AutomationPeer OnCreateAutomationPeer() => new BrightnessBoxAutomationPeer(this);

    /// <summary>Sets the value the way a key press would, for assistive technology.</summary>
    internal void SetValueFromAutomation(int value)
    {
        if (!IsEnabled || !IsInteractionEnabled)
        {
            throw new ElementNotEnabledException();
        }

        Value = Math.Clamp(value, 0, 100);
        ValueCommitRequested?.Invoke(this, EventArgs.Empty);
    }

    private static void OnValueChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (UIElementAutomationPeer.FromElement((BrightnessBox)dependencyObject) is BrightnessBoxAutomationPeer peer)
        {
            peer.RaiseValueChanged((int)e.OldValue, (int)e.NewValue);
        }
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
        Value = ValueFromY(y - ScreenInset, ActualHeight - ScreenInset * 2);
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

/// <summary>Exposes a <see cref="BrightnessBox"/> to screen readers as a 0–100 slider.</summary>
internal sealed class BrightnessBoxAutomationPeer(BrightnessBox owner)
    : FrameworkElementAutomationPeer(owner), IRangeValueProvider
{
    private BrightnessBox Box => (BrightnessBox)Owner;

    public double Value => Box.Value;
    public double Minimum => 0;
    public double Maximum => 100;
    public double SmallChange => 1;
    public double LargeChange => 5;
    public bool IsReadOnly => !Box.IsEnabled || !Box.IsInteractionEnabled;

    public void SetValue(double value) => Box.SetValueFromAutomation((int)Math.Round(value, MidpointRounding.AwayFromZero));

    public override object? GetPattern(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.RangeValue ? this : base.GetPattern(patternInterface);

    internal void RaiseValueChanged(int oldValue, int newValue) =>
        RaisePropertyChangedEvent(RangeValuePatternIdentifiers.ValueProperty, (double)oldValue, (double)newValue);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Slider;

    protected override string GetClassNameCore() => nameof(BrightnessBox);

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;
}
