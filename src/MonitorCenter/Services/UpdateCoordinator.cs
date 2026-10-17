namespace MonitorCenter.Services;

internal enum UpdateStage { Idle, Checking, Downloading, NoUpdate, Unavailable, Failed, Restarting }

internal sealed record UpdateStatus(UpdateStage Stage, string Message, int Progress = 0);

/// <summary>
/// Downloads and installs updates when the user clicks the update item in the tray menu, and checks quietly in the
/// background so that item can say when an update is available. Background checks never download or install
/// anything. Status changes are raised on the caller's synchronization context.
/// </summary>
internal sealed class UpdateCoordinator(IAppUpdater updater, Action shutdown, TimeProvider? clock = null) : IDisposable
{
    /// <summary>The shortest time between background checks, which keeps them well inside GitHub's rate limit.</summary>
    internal static readonly TimeSpan MinimumCheckInterval = TimeSpan.FromMinutes(30);

    internal static string UnavailableMessage => Strings.UpdatesUnavailable;
    internal static string CheckingMessage => Strings.CheckingForUpdates;
    internal static string UpToDateMessage => Strings.UpToDate;
    internal static string RestartingMessage => Strings.RestartingToUpdate;

    private readonly CancellationTokenSource _lifetime = new();
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private DateTimeOffset? _lastCheck;
    private Task? _backgroundCheck;
    private int _running;
    private bool _disposed;

    public UpdateStatus Status { get; private set; } = new(UpdateStage.Idle, Strings.CheckForUpdates);

    /// <summary>The newer version found by the last check, or null when none is known.</summary>
    public string? AvailableVersion { get; private set; }

    public event Action<UpdateStatus>? StatusChanged;

    public event Action<string?>? AvailableVersionChanged;

    /// <summary>
    /// Checks whether an update is available without downloading it. Does nothing when the app cannot update, an
    /// update is already known, a user-requested run is in progress, or the last check was less than
    /// <see cref="MinimumCheckInterval"/> ago. Failures are ignored and keep the last known result.
    /// </summary>
    public Task CheckInBackgroundAsync()
    {
        if (_disposed || _running != 0 || _backgroundCheck is { IsCompleted: false } || !updater.CanUpdate) return Task.CompletedTask;
        if (updater.PendingVersion is { } pending)
        {
            SetAvailableVersion(pending);
            return Task.CompletedTask;
        }
        if (AvailableVersion is not null) return Task.CompletedTask;
        if (_lastCheck is { } last && _clock.GetUtcNow() - last < MinimumCheckInterval) return Task.CompletedTask;

        _backgroundCheck = CheckAsync();
        return _backgroundCheck;

        async Task CheckAsync()
        {
            var token = _lifetime.Token;
            try
            {
                _lastCheck = _clock.GetUtcNow();
                var version = await updater.CheckAsync(token);
                if (!token.IsCancellationRequested) SetAvailableVersion(version);
            }
            catch
            {
                // The user is not waiting on a background check; the next one or a click will try again.
            }
        }
    }

    /// <summary>
    /// Checks for an update unless a background check already found one, then downloads it and restarts into it.
    /// Calls made while a run is in progress return straight away.
    /// </summary>
    public async Task RunAsync()
    {
        if (_disposed || Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        var restarting = false;
        var token = _lifetime.Token;
        try
        {
            if (_backgroundCheck is { IsCompleted: false } backgroundCheck)
            {
                Report(new(UpdateStage.Checking, CheckingMessage));
                await backgroundCheck;
                token.ThrowIfCancellationRequested();
            }

            if (!updater.CanUpdate)
            {
                Report(new(UpdateStage.Unavailable, UnavailableMessage));
                return;
            }

            if (updater.PendingVersion is null)
            {
                // An update found by a background check is downloaded straight away instead of checking again.
                var version = AvailableVersion;
                if (version is null)
                {
                    Report(new(UpdateStage.Checking, CheckingMessage));
                    _lastCheck = _clock.GetUtcNow();
                    version = await updater.CheckAsync(token);
                    token.ThrowIfCancellationRequested();
                    SetAvailableVersion(version);
                }

                if (version is null)
                {
                    Report(new(UpdateStage.NoUpdate, UpToDateMessage));
                    return;
                }

                Report(new(UpdateStage.Downloading, Strings.DownloadStarting(version)));
                var downloading = true;
                var progress = new Progress<int>(percent =>
                {
                    if (_disposed || !downloading || Status.Stage != UpdateStage.Downloading) return;
                    var clamped = Math.Clamp(percent, 0, 100);
                    Report(new(UpdateStage.Downloading, Strings.DownloadProgress(version, clamped), clamped));
                });
                try { await updater.DownloadAsync(progress, token); }
                finally { downloading = false; }
                token.ThrowIfCancellationRequested();
                Report(new(UpdateStage.Downloading, Strings.DownloadComplete(version), 100));
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
            Report(new(UpdateStage.Failed, Strings.UpdateFailed(exception.Message)));
        }
        finally
        {
            if (!restarting) Interlocked.Exchange(ref _running, 0);
        }
    }

    private void SetAvailableVersion(string? version)
    {
        if (_disposed || version == AvailableVersion) return;
        AvailableVersion = version;
        AvailableVersionChanged?.Invoke(version);
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
