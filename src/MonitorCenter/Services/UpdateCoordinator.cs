using System.ComponentModel;

namespace MonitorCenter.Services;

/// <summary>
/// Runs update checks, both from the tray menu and in the background, and exposes the tray menu text for them.
/// </summary>
internal sealed class UpdateCoordinator : INotifyPropertyChanged, IDisposable
{
    internal const string IdleText = "Check for updates";
    internal const string CheckingText = "Checking for updates…";
    internal const string NoUpdatesText = "No new updates";
    internal const string UpdatingText = "Updating…";
    internal const string UpdatedText = "Updated!";
    internal const string FailedText = "Update failed";
    internal const string UnavailableText = "Updates need the installed app";

    internal static readonly TimeSpan MessageDuration = TimeSpan.FromSeconds(3);
    internal static readonly TimeSpan UpdatedDuration = TimeSpan.FromSeconds(1.5);
    internal static readonly TimeSpan FirstBackgroundCheckDelay = TimeSpan.FromMinutes(2);
    internal static readonly TimeSpan BackgroundCheckInterval = TimeSpan.FromHours(24);

    private readonly IAppUpdater _updater;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _lifetime = new();
    private Task _operation = Task.CompletedTask;
    private bool _isVisible;
    private bool _isDownloading;
    private string? _status;
    private bool _disposed;

    public UpdateCoordinator(IAppUpdater updater, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _updater = updater;
        _delay = delay ?? Task.Delay;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when a background check has downloaded an update, with the new version.</summary>
    public event EventHandler<string>? UpdateReady;

    /// <summary>Raised once the updater is waiting for the app to exit so it can install and restart.</summary>
    public event EventHandler? RestartRequested;

    public string MenuText =>
        (_isVisible ? _status : null) ??
        (_updater.PendingVersion is { } version ? $"Restart to update to {version}" : IdleText);

    /// <summary>Checks for an update and, if there is one, downloads it and restarts into it, showing progress in the menu.</summary>
    public Task CheckNowAsync()
    {
        if (!_isVisible && !_operation.IsCompleted)
        {
            // Joining a background check: reveal its progress in the menu.
            _isVisible = true;
            NotifyMenuTextChanged();
        }
        _isVisible = true;
        return Start();
    }

    /// <summary>Checks for an update and downloads it without showing progress or restarting.</summary>
    public Task CheckInBackgroundAsync() => Start();

    public void StartBackgroundChecks() => _ = RunBackgroundChecksAsync();

    private Task Start()
    {
        if (_disposed) return Task.CompletedTask;
        if (_operation.IsCompleted)
        {
            _operation = RunAsync(_lifetime.Token);
        }
        return _operation;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var restarting = false;
        try
        {
            if (!_updater.CanUpdate)
            {
                await ShowResultAsync(UnavailableText, cancellationToken);
                return;
            }

            if (_updater.PendingVersion is null)
            {
                SetStatus(CheckingText);
                if (await _updater.CheckForUpdateAsync(cancellationToken) is null)
                {
                    await ShowResultAsync(NoUpdatesText, cancellationToken);
                    return;
                }

                _isDownloading = true;
                SetStatus(UpdatingText);
                var progress = new Progress<int>(percent =>
                {
                    if (_isDownloading) SetStatus($"{UpdatingText} {Math.Clamp(percent, 0, 100)}%");
                });
                await _updater.DownloadUpdateAsync(progress, cancellationToken);
                _isDownloading = false;
            }

            if (!_isVisible)
            {
                if (_updater.PendingVersion is { } version) UpdateReady?.Invoke(this, version);
                return;
            }

            SetStatus(UpdatedText);
            await _delay(UpdatedDuration, cancellationToken);
            _updater.ApplyAfterExit();
            restarting = true;
            RestartRequested?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            _isDownloading = false;
            await ShowResultAsync(FailedText, cancellationToken);
        }
        finally
        {
            _isDownloading = false;
            // While the app shuts down for the restart, keep showing that the update succeeded.
            if (!restarting)
            {
                _isVisible = false;
                _status = null;
                NotifyMenuTextChanged();
            }
        }
    }

    /// <summary>Shows a result in the menu for a few seconds. Background checks finish silently instead.</summary>
    private async Task ShowResultAsync(string text, CancellationToken cancellationToken)
    {
        SetStatus(text);
        if (!_isVisible) return;
        try { await _delay(MessageDuration, cancellationToken); }
        catch (OperationCanceledException) { }
    }

    private async Task RunBackgroundChecksAsync()
    {
        try
        {
            await _delay(FirstBackgroundCheckDelay, _lifetime.Token);
            while (!_lifetime.IsCancellationRequested)
            {
                await CheckInBackgroundAsync();
                await _delay(BackgroundCheckInterval, _lifetime.Token);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SetStatus(string text)
    {
        _status = text;
        NotifyMenuTextChanged();
    }

    private void NotifyMenuTextChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MenuText)));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
