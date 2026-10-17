using System.IO;
using System.Windows;
using System.Windows.Threading;
using MonitorCenter.Services;
using MonitorCenter.UI;

namespace MonitorCenter;

public partial class App : System.Windows.Application
{
    private static readonly TimeSpan FirstUpdateCheckDelay = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(8);

    private SingleInstanceCoordinator? _singleInstance;
    private MonitorService? _monitorService;
    private MonitorCoordinator? _monitorCoordinator;
    private FlyoutViewModel? _viewModel;
    private FlyoutWindow? _flyoutWindow;
    private TrayIconController? _trayIcon;
    private StartupRegistration? _startupRegistration;
    private UpdateCoordinator? _updates;
    private UpdateWindow? _updateWindow;
    private DispatcherTimer? _updateCheckTimer;
    private string? _notifiedUpdateVersion;
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
        _updates = new UpdateCoordinator(new VelopackAppUpdater(), () => OnExitRequested(this, EventArgs.Empty));
        _updates.StatusChanged += status => _updateWindow?.Render(status);
        _trayIcon = new TrayIconController(_flyoutWindow, _startupRegistration);
        _trayIcon.UpdateRequested += OnUpdateRequested;
        _trayIcon.ExitRequested += OnExitRequested;
        _trayIcon.MenuOpening += OnTrayMenuOpening;
        _updates.AvailableVersionChanged += OnAvailableUpdateChanged;
        StartUpdateChecks();

        if (Program.UpdatedToVersion is { } updatedVersion)
        {
            _trayIcon.ShowInfo($"MonitorCenter was updated to version {updatedVersion}.");
        }

        try
        {
            await _monitorCoordinator.InitializeAsync();
        }
        catch (Exception exception)
        {
            _trayIcon?.ShowError($"MonitorCenter could not initialize: {exception.Message}");
        }

        // Exit/update can be requested while the initial discovery is still awaiting hardware.
        if (_isShuttingDown != 0) return;

#if !DEBUG
        if (!options.IsStartupLaunch)
        {
            try
            {
                _startupRegistration.EnsureDefaultEnabled();
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                _trayIcon?.ShowError("Start with Windows could not be enabled.");
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

    private async void OnUpdateRequested(object? sender, EventArgs e)
    {
        if (_isShuttingDown != 0 || _updates is null) return;
        if (_updateWindow is null)
        {
            _updateWindow = new UpdateWindow();
            _updateWindow.Closed += (_, _) => _updateWindow = null;
        }
        _updateWindow.Render(_updates.Status);
        _updateWindow.Show();
        _updateWindow.Activate();
        await _updates.RunAsync();
    }

    /// <summary>
    /// Checks for updates shortly after startup and then every few hours, so the tray menu can offer to install one.
    /// Nothing is downloaded until the user chooses to install it.
    /// </summary>
    private void StartUpdateChecks()
    {
        _updateCheckTimer = new DispatcherTimer { Interval = FirstUpdateCheckDelay };
        _updateCheckTimer.Tick += (_, _) =>
        {
            _updateCheckTimer.Interval = UpdateCheckInterval;
            _ = _updates?.CheckInBackgroundAsync();
        };
        _updateCheckTimer.Start();
    }

    // The coordinator limits these to one per half hour, so opening the menu often does not hit GitHub's rate limit.
    private void OnTrayMenuOpening(object? sender, EventArgs e) => _ = _updates?.CheckInBackgroundAsync();

    private void OnAvailableUpdateChanged(string? version)
    {
        _trayIcon?.SetAvailableUpdate(version);
        // Mention each new version once, unless the user is already watching the update window.
        if (version is null || version == _notifiedUpdateVersion || _updateWindow is not null) return;
        _notifiedUpdateVersion = version;
        _trayIcon?.ShowInfo(
            $"MonitorCenter {version} is available. Click here or use the tray menu to install it.",
            () => OnUpdateRequested(this, EventArgs.Empty));
    }

    private async void OnExitRequested(object? sender, EventArgs e)
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0)
        {
            return;
        }

        if (_trayIcon is not null)
        {
            _trayIcon.UpdateRequested -= OnUpdateRequested;
            _trayIcon.ExitRequested -= OnExitRequested;
            _trayIcon.MenuOpening -= OnTrayMenuOpening;
            _trayIcon.Dispose();
            _trayIcon = null;
        }

        _updateCheckTimer?.Stop();
        _updateCheckTimer = null;
        _updates?.Dispose();
        _updates = null;
        _updateWindow?.Close();

        _flyoutWindow?.BeginShutdown();
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
