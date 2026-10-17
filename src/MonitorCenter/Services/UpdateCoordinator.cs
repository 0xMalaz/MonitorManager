namespace MonitorCenter.Services;

internal enum UpdateStage { Idle, Checking, Downloading, NoUpdate, Unavailable, Failed, Restarting }

internal sealed record UpdateStatus(UpdateStage Stage, string Message, int Progress = 0);

/// <summary>
/// Checks for, downloads and installs updates when the user clicks "Check for updates" in the tray menu.
/// MonitorCenter never does any of this on its own. Status changes are raised on the caller's synchronization context.
/// </summary>
internal sealed class UpdateCoordinator(IAppUpdater updater, Action shutdown) : IDisposable
{
    internal const string UnavailableMessage =
        "Updates are unavailable in development or unpackaged builds. Install MonitorCenter-Setup.exe first.";
    internal const string CheckingMessage = "Checking for updates…";
    internal const string UpToDateMessage = "MonitorCenter is up to date.";
    internal const string RestartingMessage = "Restarting to apply the update…";

    private readonly CancellationTokenSource _lifetime = new();
    private int _running;
    private bool _disposed;

    public UpdateStatus Status { get; private set; } = new(UpdateStage.Idle, "Check for updates");

    public event Action<UpdateStatus>? StatusChanged;

    /// <summary>
    /// Checks for an update and, if there is one, downloads it and restarts into it. Calls made while a run is in
    /// progress return straight away.
    /// </summary>
    public async Task RunAsync()
    {
        if (_disposed || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        var restarting = false;
        var token = _lifetime.Token;
        try
        {
            if (!updater.CanUpdate)
            {
                Report(new(UpdateStage.Unavailable, UnavailableMessage));
                return;
            }

            if (updater.PendingVersion is null)
            {
                Report(new(UpdateStage.Checking, CheckingMessage));
                var version = await updater.CheckAsync(token);
                token.ThrowIfCancellationRequested();
                if (version is null)
                {
                    Report(new(UpdateStage.NoUpdate, UpToDateMessage));
                    return;
                }

                Report(new(UpdateStage.Downloading, $"Downloading {version}…"));
                var downloading = true;
                var progress = new Progress<int>(percent =>
                {
                    if (_disposed || !downloading || Status.Stage != UpdateStage.Downloading) return;
                    var clamped = Math.Clamp(percent, 0, 100);
                    Report(new(UpdateStage.Downloading, $"Downloading {version}: {clamped}%", clamped));
                });
                try { await updater.DownloadAsync(progress, token); }
                finally { downloading = false; }
                token.ThrowIfCancellationRequested();
                Report(new(UpdateStage.Downloading, $"Downloaded {version}: 100%", 100));
            }

            token.ThrowIfCancellationRequested();
            updater.ApplyAfterExit();
            restarting = true;
            Report(new(UpdateStage.Restarting, RestartingMessage));
            shutdown();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Report(new(UpdateStage.Failed, $"Update failed: {exception.Message}\nChoose Check for updates to retry."));
        }
        finally
        {
            if (!restarting) Interlocked.Exchange(ref _running, 0);
        }
    }

    private void Report(UpdateStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(status);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // The source is not disposed: an outstanding check may still read its token after cancellation.
        _lifetime.Cancel();
    }
}
