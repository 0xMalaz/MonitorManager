using Velopack;
using Velopack.Sources;

namespace MonitorCenter.Services;

internal interface IAppUpdater
{
    /// <summary>False when the app was not installed through Velopack, for example a development build.</summary>
    bool CanUpdate { get; }

    /// <summary>The version that has been downloaded and is waiting for a restart, if any.</summary>
    string? PendingVersion { get; }

    /// <summary>Returns the newer version that is available, or null when the app is up to date.</summary>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the update found by the last <see cref="CheckAsync"/>.</summary>
    Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken);

    /// <summary>Starts the updater, which installs the pending update once this process exits and then restarts it.</summary>
    void ApplyAfterExit();
}

internal sealed class VelopackAppUpdater : IAppUpdater
{
    internal const string RepositoryUrl = "https://github.com/0xMalaz/MonitorManager";

    /// <summary>Points updates at a local folder or URL instead of GitHub, for testing releases before publishing them.</summary>
    internal const string SourceOverrideVariable = "MONITORCENTER_UPDATE_SOURCE";

    private readonly UpdateManager _manager = CreateManager();
    private UpdateInfo? _available;

    public bool CanUpdate => _manager.IsInstalled;

    public string? PendingVersion => CanUpdate ? _manager.UpdatePendingRestart?.Version.ToString() : null;

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _available = await _manager.CheckForUpdatesAsync().ConfigureAwait(false);
        return _available?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        if (_available is null)
        {
            throw new InvalidOperationException("There is no update to download.");
        }

        return _manager.DownloadUpdatesAsync(_available, progress.Report, cancellationToken);
    }

    private static UpdateManager CreateManager()
    {
        var source = Environment.GetEnvironmentVariable(SourceOverrideVariable);
        return string.IsNullOrWhiteSpace(source)
            ? new UpdateManager(new GithubSource(RepositoryUrl, accessToken: null, prerelease: false))
            : new UpdateManager(source);
    }

    public void ApplyAfterExit()
    {
        var pending = _manager.UpdatePendingRestart ??
                      throw new InvalidOperationException("There is no downloaded update to apply.");
        // --startup keeps the restarted app in the tray instead of opening the flyout.
        _manager.WaitExitThenApplyUpdates(pending, silent: false, restart: true, ["--startup"]);
    }
}
