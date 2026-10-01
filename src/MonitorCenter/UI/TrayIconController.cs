using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using MonitorCenter.Interop;
using MonitorCenter.Services;
using FormsContextMenuStrip = System.Windows.Forms.ContextMenuStrip;
using FormsToolStripMenuItem = System.Windows.Forms.ToolStripMenuItem;

namespace MonitorCenter.UI;

internal sealed class TrayIconController : IDisposable
{
    private static readonly Guid IconGuid = new("9B151928-5408-48F9-A546-99938B439ECC");
    private const uint CallbackMessage = NativeMethods.WM_APP + 41;

    private readonly FlyoutWindow _flyoutWindow;
    private readonly StartupRegistration _startupRegistration;
    private readonly HwndSource _messageSource;
    private readonly Icon _icon;
    private readonly FormsContextMenuStrip _contextMenu;
    private readonly FormsToolStripMenuItem _startupItem;
    private readonly uint _taskbarCreatedMessage;
    private bool _disposed;

    public TrayIconController(
        FlyoutWindow flyoutWindow,
        StartupRegistration startupRegistration)
    {
        _flyoutWindow = flyoutWindow;
        _startupRegistration = startupRegistration;
        _icon = CreateTrayIcon();

        _messageSource = new HwndSource(new HwndSourceParameters("MonitorCenter.TrayIcon")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        });
        _messageSource.AddHook(WindowProcedure);
        _taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

        var openItem = new FormsToolStripMenuItem("Open flyout");
        openItem.Click += async (_, _) => await _flyoutWindow.ShowFlyoutAsync();
        var refreshItem = new FormsToolStripMenuItem("Refresh displays");
        refreshItem.Click += async (_, _) => await _flyoutWindow.RefreshAsync();
        _startupItem = new FormsToolStripMenuItem("Start with Windows") { CheckOnClick = true };
        _startupItem.Click += (_, _) => SetStartupPreference(_startupItem.Checked);
        var exitItem = new FormsToolStripMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _contextMenu = new FormsContextMenuStrip();
        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(refreshItem);
        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _contextMenu.Items.Add(_startupItem);
        _contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        _contextMenu.Items.Add(exitItem);
        _contextMenu.Opening += (_, _) => RefreshStartupCheckmark();
        ApplyContextMenuTheme();
        ThemeManager.ThemeChanged += OnThemeChanged;

        AddIcon();
    }

    public event EventHandler? ExitRequested;

    public void ShowError(string message)
    {
        if (_disposed) return;
        var data = CreateData(NativeMethods.NIF_INFO | NativeMethods.NIF_GUID);
        data.InfoTitle = "MonitorCenter";
        data.Info = message.Length <= 255 ? message : message[..255];
        data.InfoFlags = 2;
        NativeMethods.ShellNotifyIcon(NativeMethods.NIM_MODIFY, ref data);
    }

    private void AddIcon()
    {
        var data = CreateData(
            NativeMethods.NIF_MESSAGE |
            NativeMethods.NIF_ICON |
            NativeMethods.NIF_TIP |
            NativeMethods.NIF_GUID);
        if (!NativeMethods.ShellNotifyIcon(NativeMethods.NIM_ADD, ref data))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not create the notification icon");
        }
        data.TimeoutOrVersion = NativeMethods.NOTIFYICON_VERSION_4;
        NativeMethods.ShellNotifyIcon(NativeMethods.NIM_SETVERSION, ref data);
    }

    private NativeMethods.NotifyIconData CreateData(uint flags) => new()
    {
        Size = (uint)Marshal.SizeOf<NativeMethods.NotifyIconData>(),
        Window = _messageSource.Handle,
        Id = 1,
        Flags = flags,
        CallbackMessage = CallbackMessage,
        Icon = _icon.Handle,
        Tip = "MonitorCenter",
        Info = string.Empty,
        InfoTitle = string.Empty,
        GuidItem = IconGuid
    };

    private IntPtr WindowProcedure(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)message == _taskbarCreatedMessage)
        {
            AddIcon();
            handled = true;
            return IntPtr.Zero;
        }
        if (message != CallbackMessage)
        {
            return IntPtr.Zero;
        }

        var notification = unchecked((ushort)lParam.ToInt64());
        if (notification == NativeMethods.WM_LBUTTONUP)
        {
            handled = true;
            _ = _flyoutWindow.ToggleAsync();
        }
        else if (notification is NativeMethods.WM_RBUTTONUP or NativeMethods.WM_CONTEXTMENU)
        {
            handled = true;
            NativeMethods.SetForegroundWindow(_messageSource.Handle);
            _contextMenu.Show(System.Windows.Forms.Cursor.Position);
            NativeMethods.PostMessage(_messageSource.Handle, NativeMethods.WM_NULL, IntPtr.Zero, IntPtr.Zero);
        }
        return IntPtr.Zero;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyContextMenuTheme();

    private void ApplyContextMenuTheme()
    {
        var light = ThemeManager.IsLightTheme;
        var background = light ? Color.FromArgb(247, 249, 252) : Color.FromArgb(32, 36, 43);
        var foreground = light ? Color.FromArgb(24, 32, 42) : Color.FromArgb(247, 249, 252);
        _contextMenu.BackColor = background;
        _contextMenu.ForeColor = foreground;
        _contextMenu.Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);
        _contextMenu.Renderer = new System.Windows.Forms.ToolStripProfessionalRenderer(new TrayMenuColorTable(light));
        foreach (System.Windows.Forms.ToolStripItem item in _contextMenu.Items)
        {
            item.BackColor = background;
            item.ForeColor = foreground;
        }
    }

    private void SetStartupPreference(bool enabled)
    {
        try { _startupRegistration.SetEnabled(enabled); }
        catch
        {
            ShowError("Windows startup could not be updated.");
            RefreshStartupCheckmark();
        }
    }

    private void RefreshStartupCheckmark()
    {
        try { _startupItem.Checked = _startupRegistration.IsEnabled; }
        catch { _startupItem.Checked = false; }
    }

    private static Icon CreateTrayIcon()
    {
        using var stream = typeof(TrayIconController).Assembly.GetManifestResourceStream(
            "MonitorCenter.Assets.MonitorCenter.ico");
        if (stream is null)
        {
            throw new InvalidOperationException("The MonitorCenter tray icon resource is missing.");
        }

        using var icon = new Icon(stream, 32, 32);
        return (Icon)icon.Clone();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ThemeManager.ThemeChanged -= OnThemeChanged;
        var data = CreateData(NativeMethods.NIF_GUID);
        NativeMethods.ShellNotifyIcon(NativeMethods.NIM_DELETE, ref data);
        _contextMenu.Dispose();
        _messageSource.RemoveHook(WindowProcedure);
        _messageSource.Dispose();
        _icon.Dispose();
    }
}

internal sealed class TrayMenuColorTable(bool lightTheme) : System.Windows.Forms.ProfessionalColorTable
{
    private readonly Color _background = lightTheme ? Color.FromArgb(247, 249, 252) : Color.FromArgb(32, 36, 43);
    private readonly Color _border = lightTheme ? Color.FromArgb(143, 155, 170) : Color.FromArgb(104, 116, 133);
    private readonly Color _selection = lightTheme ? Color.FromArgb(223, 235, 246) : Color.FromArgb(52, 75, 91);

    public override Color ToolStripDropDownBackground => _background;
    public override Color ImageMarginGradientBegin => _background;
    public override Color ImageMarginGradientMiddle => _background;
    public override Color ImageMarginGradientEnd => _background;
    public override Color MenuBorder => _border;
    public override Color MenuItemBorder => _border;
    public override Color MenuItemSelected => _selection;
    public override Color MenuItemSelectedGradientBegin => _selection;
    public override Color MenuItemSelectedGradientEnd => _selection;
    public override Color MenuItemPressedGradientBegin => _selection;
    public override Color MenuItemPressedGradientEnd => _selection;
    public override Color SeparatorDark => _border;
    public override Color SeparatorLight => _background;
}
