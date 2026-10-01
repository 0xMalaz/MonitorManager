using System.IO;
using System.Windows;
using MonitorCenter.Services;
using MonitorCenter.UI;

namespace MonitorCenter;

public partial class App : System.Windows.Application
{
    private SingleInstanceCoordinator? _singleInstance;
    private MonitorService? _monitorService;
    private MonitorCoordinator? _monitorCoordinator;
    private FlyoutViewModel? _viewModel;
    private FlyoutWindow? _flyoutWindow;
    private TrayIconController? _trayIcon;
    private StartupRegistration? _startupRegistration;
    private int _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var options = CommandLineOptions.Parse(e.Args);
        _singleInstance = new SingleInstanceCoordinator();

        if (!_singleInstance.TryAcquire(OnActivationRequested))
        {
            _singleInstance.Dispose();
            _singleInstance = null;
            Shutdown();
            return;
        }

        ThemeManager.Initialize();

        _monitorService = new MonitorService();
        _monitorCoordinator = new MonitorCoordinator(_monitorService, Dispatcher);
        _viewModel = new FlyoutViewModel(_monitorCoordinator);
        _flyoutWindow = new FlyoutWindow(_viewModel);
        _flyoutWindow.EnsureNativeHandle();

        var executablePath = Environment.ProcessPath ??
                             System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ??
                             Path.Combine(AppContext.BaseDirectory, "MonitorCenter.exe");
        _startupRegistration = new StartupRegistration(executablePath);
        _trayIcon = new TrayIconController(_flyoutWindow, _startupRegistration);
        _trayIcon.ExitRequested += OnExitRequested;

        try
        {
            await _monitorCoordinator.InitializeAsync();
        }
        catch (Exception exception)
        {
            _trayIcon.ShowError($"MonitorCenter could not initialize: {exception.Message}");
        }

#if !DEBUG
        if (!options.IsStartupLaunch)
        {
            try
            {
                _startupRegistration.EnsureDefaultEnabled();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                _trayIcon.ShowError("Start with Windows could not be enabled.");
            }
        }
#endif

        if (!options.IsStartupLaunch)
        {
            await _flyoutWindow.ShowFlyoutAsync();
        }
    }

    private void OnActivationRequested()
    {
        Dispatcher.BeginInvoke(async () =>
        {
            if (_flyoutWindow is not null)
            {
                await _flyoutWindow.ShowFlyoutAsync();
            }
        });
    }

    private async void OnExitRequested(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0)
        {
            return;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.ExitRequested -= OnExitRequested;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _flyoutWindow?.Hide();
        if (_monitorCoordinator is not null)
        {
            await _monitorCoordinator.DisposeAsync();
            _monitorCoordinator = null;
        }
        _viewModel = null;

        if (_monitorService is not null)
        {
            await _monitorService.DisposeAsync();
            _monitorService = null;
        }

        if (_flyoutWindow is not null)
        {
            _flyoutWindow.AllowCloseAndClose();
        }
        _flyoutWindow = null;
        _singleInstance?.Dispose();
        _singleInstance = null;
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIcon?.Dispose();
        _singleInstance?.Dispose();
        ThemeManager.Shutdown();
        base.OnExit(e);
    }
}
