using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using MonitorCenter.Interop;
using MonitorCenter.Services;
using WpfContextMenu = System.Windows.Controls.ContextMenu;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfSeparator = System.Windows.Controls.Separator;

namespace MonitorCenter.UI;

internal sealed class TrayIconController : IDisposable
{
    private static readonly Guid IconGuid = new("9B151928-5408-48F9-A546-99938B439ECC");
    private const uint CallbackMessage = NativeMethods.WM_APP + 41;
    private const int TrayIconSize = 32;

    private readonly FlyoutWindow _flyoutWindow;
    private readonly StartupRegistration _startupRegistration;
    private readonly HwndSource _messageSource;
    private readonly IntPtr _icon;
    private readonly WpfContextMenu _contextMenu;
    private readonly WpfMenuItem _startupItem;
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

        var openItem = CreateMenuItem("Open flyout");
        openItem.Click += async (_, _) => await _flyoutWindow.ShowFlyoutAsync();
        var refreshItem = CreateMenuItem("Refresh displays");
        refreshItem.Click += async (_, _) => await _flyoutWindow.RefreshAsync();
        _startupItem = CreateMenuItem("Start with Windows");
        _startupItem.IsCheckable = true;
        _startupItem.Click += (_, _) => SetStartupPreference(_startupItem.IsChecked);
        var exitItem = CreateMenuItem("Exit");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);

        _contextMenu = new WpfContextMenu
        {
            Placement = PlacementMode.MousePoint
        };
        _contextMenu.SetResourceReference(FrameworkElement.StyleProperty, "TrayContextMenuStyle");
        _contextMenu.Items.Add(openItem);
        _contextMenu.Items.Add(refreshItem);
        _contextMenu.Items.Add(CreateSeparator());
        _contextMenu.Items.Add(_startupItem);
        _contextMenu.Items.Add(CreateSeparator());
        _contextMenu.Items.Add(exitItem);

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
        Icon = _icon,
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
            ShowContextMenu();
        }
        return IntPtr.Zero;
    }

    private void ShowContextMenu()
    {
        RefreshStartupCheckmark();
        _contextMenu.IsOpen = true;

        // The menu must own the foreground so that clicking anywhere else dismisses it.
        if (PresentationSource.FromVisual(_contextMenu) is HwndSource menuSource)
        {
            NativeMethods.SetForegroundWindow(menuSource.Handle);
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
        try { _startupItem.IsChecked = _startupRegistration.IsEnabled; }
        catch { _startupItem.IsChecked = false; }
    }

    private static WpfMenuItem CreateMenuItem(string header)
    {
        var item = new WpfMenuItem { Header = header };
        item.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuItemStyle");
        return item;
    }

    private static WpfSeparator CreateSeparator()
    {
        var separator = new WpfSeparator();
        separator.SetResourceReference(FrameworkElement.StyleProperty, "TrayMenuSeparatorStyle");
        return separator;
    }

    internal static IntPtr CreateTrayIcon()
    {
        using var stream = typeof(TrayIconController).Assembly.GetManifestResourceStream(
            "MonitorCenter.Assets.MonitorCenter.ico");
        if (stream is null)
        {
            throw new InvalidOperationException("The MonitorCenter tray icon resource is missing.");
        }

        var image = ReadIconImage(stream, TrayIconSize);
        var icon = NativeMethods.CreateIconFromResourceEx(
            image,
            (uint)image.Length,
            isIcon: true,
            NativeMethods.ICON_RESOURCE_VERSION,
            TrayIconSize,
            TrayIconSize,
            NativeMethods.LR_DEFAULTCOLOR);
        if (icon == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows could not load the tray icon");
        }

        return icon;
    }

    /// <summary>
    /// Returns the image data of the .ico frame closest to <paramref name="size"/>, preferring larger frames
    /// so Windows scales down rather than up.
    /// </summary>
    internal static byte[] ReadIconImage(Stream stream, int size)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        reader.ReadUInt16();
        if (reader.ReadUInt16() != 1)
        {
            throw new InvalidDataException("The tray icon resource is not an icon file.");
        }

        var count = reader.ReadUInt16();
        (int Size, uint Length, uint Offset)? best = null;
        for (var index = 0; index < count; index++)
        {
            var widthByte = reader.ReadByte();
            reader.ReadBytes(7);
            var length = reader.ReadUInt32();
            var offset = reader.ReadUInt32();
            var width = widthByte == 0 ? 256 : widthByte;
            if (best is null || Score(width) < Score(best.Value.Size))
            {
                best = (width, length, offset);
            }
        }

        if (best is not { } frame)
        {
            throw new InvalidDataException("The tray icon resource has no images.");
        }

        stream.Position = frame.Offset;
        return reader.ReadBytes((int)frame.Length);

        int Score(int width) => width >= size ? width - size : 1000 + size - width;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var data = CreateData(NativeMethods.NIF_GUID);
        NativeMethods.ShellNotifyIcon(NativeMethods.NIM_DELETE, ref data);
        _contextMenu.IsOpen = false;
        _messageSource.RemoveHook(WindowProcedure);
        _messageSource.Dispose();
        NativeMethods.DestroyIcon(_icon);
    }
}
