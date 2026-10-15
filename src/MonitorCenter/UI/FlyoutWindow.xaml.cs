using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using MonitorCenter.Interop;
using WpfButton = System.Windows.Controls.Button;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace MonitorCenter.UI;

public partial class FlyoutWindow : Window
{
    private readonly FlyoutViewModel _viewModel;
    private readonly SemaphoreSlim _visibilityGate = new(1, 1);
    private readonly DispatcherTimer _displayChangeTimer;
    private readonly DispatcherTimer _wakeTimer;
    private HwndSource? _windowSource;
    private IntPtr _monitorNotification;
    private IntPtr _displayStateNotification;
    private bool _sessionNotificationRegistered;
    private int? _displayState;
    private bool _isTileMenuOpen;
    private bool _allowClose;
    private bool _disposed;

    internal FlyoutWindow(FlyoutViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        SourceInitialized += OnSourceInitialized;
        Deactivated += (_, _) => { if (!_allowClose && !_isTileMenuOpen) Hide(); };
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                if (_viewModel.IsRenaming)
                {
                    _viewModel.CancelRenames();
                    ResizeAndReposition();
                }
                else
                {
                    Hide();
                }
                e.Handled = true;
            }
        };
        IsVisibleChanged += (_, _) => { if (!IsVisible) _viewModel.CancelRenames(); };
        Closing += OnClosing;
        ThemeManager.ThemeChanged += OnThemeChanged;

        _displayChangeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _displayChangeTimer.Tick += async (_, _) =>
        {
            _displayChangeTimer.Stop();
            await RefreshAsync(reprobeKnownDisplays: false);
        };

        // Displays take a few seconds after waking before they answer DDC/CI again.
        _wakeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _wakeTimer.Tick += async (_, _) =>
        {
            _wakeTimer.Stop();
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
            await row.CommitAsync();
        }
    }

    private async void ProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: ProfileTileViewModel { IsRenaming: false } tile })
        {
            await _viewModel.ApplyProfileAsync(tile.Profile);
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

    private async void ProfileSaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: ProfileTileViewModel tile })
        {
            await _viewModel.SaveProfileNameAsync(tile);
            ResizeAndReposition();
        }
    }

    private async void DisplaySaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { DataContext: MonitorRowViewModel row })
        {
            await _viewModel.SaveDisplayNameAsync(row);
            ResizeAndReposition();
        }
    }

    private void TileMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton { ContextMenu: { } menu } button)
        {
            _isTileMenuOpen = true;
            menu.PlacementTarget = button;
            menu.IsOpen = true;
            e.Handled = true;
        }
    }

    private void RenameProfileMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem { CommandParameter: ProfileTileViewModel tile })
        {
            _viewModel.StartProfileRename(tile);
            ResizeAndReposition();
        }
    }

    private async void DeleteProfileMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem { CommandParameter: ProfileTileViewModel tile })
        {
            await _viewModel.DeleteProfileAsync(tile);
            ResizeAndReposition();
        }
    }

    private void RenameDisplayMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is WpfMenuItem { CommandParameter: MonitorRowViewModel row })
        {
            _viewModel.StartDisplayRename(row);
            ResizeAndReposition();
        }
    }

    private void TileContextMenu_OnClosed(object sender, RoutedEventArgs e)
    {
        _isTileMenuOpen = false;
        if (!IsVisible)
        {
            return;
        }

        // Deactivation is ignored while the menu is open. If the menu closed because the user moved to
        // another app, dismiss the flyout as that deactivation would have; otherwise take focus back.
        if (IsForegroundInThisProcess())
        {
            Activate();
        }
        else
        {
            Hide();
        }
    }

    private static bool IsForegroundInThisProcess()
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        return processId == (uint)Environment.ProcessId;
    }

    private void RenameTextBox_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is System.Windows.Controls.TextBox { IsVisible: true } textBox)
        {
            // Wait for the context menu to close and give focus back to the flyout.
            Dispatcher.BeginInvoke(() =>
            {
                Activate();
                textBox.Focus();
                textBox.SelectAll();
            }, DispatcherPriority.Input);
        }
    }

    private async void ProfileRenameTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is FrameworkElement { DataContext: ProfileTileViewModel tile })
        {
            e.Handled = true;
            await _viewModel.SaveProfileNameAsync(tile);
            ResizeAndReposition();
        }
    }

    private async void DisplayRenameTextBox_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is FrameworkElement { DataContext: MonitorRowViewModel row })
        {
            e.Handled = true;
            await _viewModel.SaveDisplayNameAsync(row);
            ResizeAndReposition();
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
        else if (IsWakeOrUnlock(message, wParam) || IsDisplayTurnedOn(message, wParam, lParam, ref _displayState))
        {
            _wakeTimer.Stop();
            _wakeTimer.Start();
        }
        return IntPtr.Zero;
    }

    internal static bool IsWakeOrUnlock(int message, IntPtr wParam)
    {
        var eventType = unchecked((int)wParam.ToInt64());
        return message switch
        {
            NativeMethods.WM_POWERBROADCAST =>
                eventType is NativeMethods.PBT_APMRESUMEAUTOMATIC or NativeMethods.PBT_APMRESUMESUSPEND,
            NativeMethods.WM_WTSSESSION_CHANGE => eventType == NativeMethods.WTS_SESSION_UNLOCK,
            _ => false
        };
    }

    /// <summary>
    /// True when the console display state changes from off to on. Windows reports the current state as soon as
    /// the notification is registered, which only records it.
    /// </summary>
    internal static bool IsDisplayTurnedOn(int message, IntPtr wParam, IntPtr lParam, ref int? displayState)
    {
        if (message != NativeMethods.WM_POWERBROADCAST ||
            unchecked((int)wParam.ToInt64()) != NativeMethods.PBT_POWERSETTINGCHANGE ||
            lParam == IntPtr.Zero)
        {
            return false;
        }

        // POWERBROADCAST_SETTING: the setting GUID, the data length, then the data (0 off, 1 on, 2 dimmed).
        var setting = Marshal.PtrToStructure<Guid>(lParam);
        if (setting != NativeMethods.GUID_CONSOLE_DISPLAY_STATE || Marshal.ReadInt32(lParam, 16) < sizeof(int))
        {
            return false;
        }

        var previous = displayState;
        displayState = Marshal.ReadInt32(lParam, 20);
        return previous == 0 && displayState != 0;
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

        var displayState = NativeMethods.GUID_CONSOLE_DISPLAY_STATE;
        _displayStateNotification = NativeMethods.RegisterPowerSettingNotification(
            handle,
            ref displayState,
            NativeMethods.DEVICE_NOTIFY_WINDOW_HANDLE);
        _sessionNotificationRegistered = NativeMethods.WTSRegisterSessionNotification(
            handle,
            NativeMethods.NOTIFY_FOR_THIS_SESSION);
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
        _wakeTimer.Stop();
        ThemeManager.ThemeChanged -= OnThemeChanged;
        if (_monitorNotification != IntPtr.Zero)
        {
            NativeMethods.UnregisterDeviceNotification(_monitorNotification);
            _monitorNotification = IntPtr.Zero;
        }
        if (_displayStateNotification != IntPtr.Zero)
        {
            NativeMethods.UnregisterPowerSettingNotification(_displayStateNotification);
            _displayStateNotification = IntPtr.Zero;
        }
        if (_sessionNotificationRegistered)
        {
            NativeMethods.WTSUnRegisterSessionNotification(new WindowInteropHelper(this).Handle);
            _sessionNotificationRegistered = false;
        }
        _windowSource?.RemoveHook(WindowProcedure);
        _windowSource = null;
        Close();
        _visibilityGate.Dispose();
    }
}
