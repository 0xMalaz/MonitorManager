using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MonitorCenter.Interop;
using MonitorCenter.Models;
using WpfButton = System.Windows.Controls.Button;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace MonitorCenter.UI;

public partial class FlyoutWindow : Window
{
    private readonly FlyoutViewModel _viewModel;
    private readonly SemaphoreSlim _visibilityGate = new(1, 1);
    private readonly DispatcherTimer _displayChangeTimer;
    private HwndSource? _windowSource;
    private IntPtr _monitorNotification;
    private bool _isDisplayMenuOpen;
    private bool _allowClose;
    private bool _disposed;

    internal FlyoutWindow(FlyoutViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Deactivated += (_, _) => { if (!_allowClose && !_isDisplayMenuOpen) Hide(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Hide();
                e.Handled = true;
            }
        };
        Closing += OnClosing;
        ThemeManager.ThemeChanged += OnThemeChanged;

        _displayChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _displayChangeTimer.Tick += async (_, _) =>
        {
            _displayChangeTimer.Stop();
            await RefreshAsync(reprobeKnownDisplays: false);
        };
    }

    public void EnsureNativeHandle() => _ = new WindowInteropHelper(this).EnsureHandle();

    public async Task ToggleAsync()
    {
        await _visibilityGate.WaitAsync();
        try
        {
            if (IsVisible) Hide(); else await ShowFlyoutCoreAsync();
        }
        finally { _visibilityGate.Release(); }
    }

    public async Task ShowFlyoutAsync()
    {
        await _visibilityGate.WaitAsync();
        try
        {
            if (IsVisible) { Activate(); return; }
            await ShowFlyoutCoreAsync();
        }
        finally { _visibilityGate.Release(); }
    }

    public async Task RefreshAsync(bool reprobeKnownDisplays = true)
    {
        if (_disposed) return;
        NativeMethods.GetCursorPos(out var cursor);
        await _viewModel.RefreshAsync(reprobeKnownDisplays);
        if (IsVisible)
        {
            UpdateLayout();
            PositionNearTaskbar(cursor);
        }
    }

    private async Task ShowFlyoutCoreAsync()
    {
        NativeMethods.GetCursorPos(out var cursor);
        if (_viewModel.Monitors.Count == 0)
        {
            await _viewModel.RefreshAsync();
        }
        Opacity = 0;
        Show();
        UpdateLayout();
        PositionNearTaskbar(cursor);
        Opacity = 1;
        Activate();
        Focus();
    }

    private async void BrightnessBox_OnValueCommitRequested(object? sender, EventArgs e)
    {
        if (sender is BrightnessBox { DataContext: MonitorRowViewModel row })
        {
            await row.FlushAsync();
        }
    }

    private async void ProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: BrightnessProfile profile })
        {
            await _viewModel.ApplyProfileAsync(profile);
        }
    }

    private async void RefreshButton_OnClick(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void NewProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenProfileEditor();
        ResizeAndReposition();
        Dispatcher.BeginInvoke(() =>
        {
            ProfileNameTextBox.Focus();
            ProfileNameTextBox.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void DisplayMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { ContextMenu: { } menu } button)
        {
            _isDisplayMenuOpen = true;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void RenameDisplayMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem { CommandParameter: MonitorRowViewModel row })
        {
            _viewModel.OpenRenameEditor(row);
            ResizeAndReposition();
            Dispatcher.BeginInvoke(() =>
            {
                Activate();
                RenameDisplayTextBox.Focus();
                RenameDisplayTextBox.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    private void DisplayContextMenu_OnClosed(object sender, RoutedEventArgs e)
    {
        _isDisplayMenuOpen = false;
        if (IsVisible)
        {
            Activate();
        }
    }

    private async void CreateProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.CreateProfileAsync();
        ResizeAndReposition();
    }

    private async void ProfileNameTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.CanSaveNewProfile)
        {
            e.Handled = true;
            await _viewModel.CreateProfileAsync();
            ResizeAndReposition();
        }
    }

    private void CancelProfileSaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseProfileEditor();
        ResizeAndReposition();
    }

    private async void SaveDisplayNameButton_OnClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.SaveDisplayNameAsync();
        ResizeAndReposition();
    }

    private async void RenameDisplayTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _viewModel.CanSaveDisplayName)
        {
            e.Handled = true;
            await _viewModel.SaveDisplayNameAsync();
            ResizeAndReposition();
        }
    }

    private void CancelRenameButton_OnClick(object sender, RoutedEventArgs e)
    {
        _viewModel.CloseRenameEditor();
        ResizeAndReposition();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _windowSource = HwndSource.FromHwnd(handle);
        _windowSource?.AddHook(WindowProcedure);
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE,
            new IntPtr((style | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW));
        ApplySystemBackdrop(handle);
        RegisterMonitorNotifications(handle);
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        // Top-level windows receive a WM_DEVICECHANGE broadcast for every USB, Bluetooth, or dock change;
        // only monitor interface arrivals and removals warrant a display refresh.
        if (message == NativeMethods.WM_DISPLAYCHANGE ||
            (message == NativeMethods.WM_DEVICECHANGE && IsMonitorInterfaceChange(wParam, lParam)))
        {
            _displayChangeTimer.Stop();
            _displayChangeTimer.Start();
        }
        return IntPtr.Zero;
    }

    internal static bool IsMonitorInterfaceChange(IntPtr wParam, IntPtr lParam)
    {
        var eventType = unchecked((int)wParam.ToInt64());
        if (eventType is not (NativeMethods.DBT_DEVICEARRIVAL or NativeMethods.DBT_DEVICEREMOVECOMPLETE) ||
            lParam == IntPtr.Zero)
        {
            return false;
        }

        var header = Marshal.PtrToStructure<NativeMethods.DevBroadcastDeviceInterface>(lParam);
        return header.DeviceType == NativeMethods.DBT_DEVTYP_DEVICEINTERFACE &&
               header.ClassGuid == NativeMethods.GUID_DEVINTERFACE_MONITOR;
    }

    private void RegisterMonitorNotifications(IntPtr handle)
    {
        if (_monitorNotification != IntPtr.Zero)
        {
            return;
        }

        var filter = new NativeMethods.DevBroadcastDeviceInterface
        {
            Size = Marshal.SizeOf<NativeMethods.DevBroadcastDeviceInterface>(),
            DeviceType = NativeMethods.DBT_DEVTYP_DEVICEINTERFACE,
            ClassGuid = NativeMethods.GUID_DEVINTERFACE_MONITOR
        };
        _monitorNotification = NativeMethods.RegisterDeviceNotification(
            handle,
            ref filter,
            NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
    }

    private void ApplySystemBackdrop(IntPtr handle)
    {
        ThemeManager.ApplyWindowTheme(this, NativeMethods.DWMSBT_TRANSIENTWINDOW);
        var margins = new NativeMethods.Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        NativeMethods.DwmExtendFrameIntoClientArea(handle, ref margins);
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            ApplySystemBackdrop(handle);
        }
    }

    private void PositionNearTaskbar(NativeMethods.Point cursor)
    {
        var monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var info = new NativeMethods.MonitorInfoEx { Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>() };
        if (monitor == IntPtr.Zero || !NativeMethods.GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var scale = NativeMethods.GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0
            ? dpiX / 96d
            : 1d;
        var bounds = info.Monitor;
        var workArea = info.WorkArea;
        var placement = FlyoutPlacement.Calculate(
            new PixelRect(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom),
            new PixelRect(workArea.Left, workArea.Top, workArea.Right, workArea.Bottom),
            new PixelPoint(cursor.X, cursor.Y),
            new PixelSize((int)Math.Ceiling(ActualWidth * scale), (int)Math.Ceiling(ActualHeight * scale)));
        Left = placement.X / scale;
        Top = placement.Y / scale;
    }

    private void ResizeAndReposition()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!IsVisible)
            {
                return;
            }

            NativeMethods.GetCursorPos(out var cursor);
            UpdateLayout();
            PositionNearTaskbar(cursor);
        }, DispatcherPriority.Loaded);
    }

    public void AllowCloseAndClose()
    {
        _disposed = true;
        _allowClose = true;
        _displayChangeTimer.Stop();
        ThemeManager.ThemeChanged -= OnThemeChanged;
        if (_monitorNotification != IntPtr.Zero)
        {
            NativeMethods.UnregisterDeviceNotification(_monitorNotification);
            _monitorNotification = IntPtr.Zero;
        }
        _windowSource?.RemoveHook(WindowProcedure);
        _windowSource = null;
        Close();
        _visibilityGate.Dispose();
    }
}
