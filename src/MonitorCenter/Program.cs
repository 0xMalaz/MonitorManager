using MonitorCenter.Services;
using Velopack;

namespace MonitorCenter;

internal static class Program
{
    /// <summary>The version Velopack just updated to, when this launch is the restart after an update.</summary>
    internal static string? UpdatedToVersion { get; private set; }

    [STAThread]
    private static void Main()
    {
        // Velopack handles install, update and uninstall events here and exits early for them.
        VelopackApp.Build()
            .OnRestarted(version => UpdatedToVersion = version.ToString())
            .OnBeforeUninstallFastCallback(_ => new StartupRegistration(Environment.ProcessPath!).Unregister())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
