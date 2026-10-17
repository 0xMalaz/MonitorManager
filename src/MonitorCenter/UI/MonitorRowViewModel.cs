using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using MonitorCenter.Models;
using MonitorCenter.Services;

namespace MonitorCenter.UI;

internal sealed class MonitorRowViewModel : INotifyPropertyChanged, IAsyncDisposable
{
    private readonly MonitorService _monitorService;
    private readonly Dispatcher _dispatcher;
    private readonly Func<MonitorRowViewModel, int, BrightnessWriteMode, CancellationToken, Task<int>>? _brightnessWriter;
    private readonly BrightnessWriteCoordinator? _writeCoordinator;
    private int _currentPercent;
    private int _appliedPercent;
    private string? _statusText;
    private bool _suppressWrite;
    private bool _isBeingDragged;
    private bool _isRenaming;
    private string _editName = string.Empty;
    private bool _disposed;

    public MonitorRowViewModel(
        MonitorSnapshot snapshot,
        MonitorService monitorService,
        Dispatcher dispatcher,
        Func<MonitorRowViewModel, int, BrightnessWriteMode, CancellationToken, Task<int>>? brightnessWriter = null)
    {
        Snapshot = snapshot;
        _monitorService = monitorService;
        _dispatcher = dispatcher;
        _brightnessWriter = brightnessWriter;
        _currentPercent = snapshot.CurrentPercent;
        _appliedPercent = snapshot.CurrentPercent;
        _statusText = snapshot.ErrorMessage;

        if (snapshot.IsControllable)
        {
            _writeCoordinator = new BrightnessWriteCoordinator(WriteBrightnessAsync);
            _writeCoordinator.Applied += HandleApplied;
            _writeCoordinator.Failed += HandleFailed;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Raised when the user changes this display's brightness on its tile.</summary>
    public event EventHandler? UserAdjusted;

    public MonitorSnapshot Snapshot { get; private set; }
    public int AppliedPercent => _appliedPercent;
    public string DisplayName => Snapshot.DisplayName;
    public bool IsControllable => Snapshot.IsControllable;
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public string? CompactStatusText => HasStatus
        ? Snapshot.Status == MonitorSupportStatus.Reconnecting ? Strings.ReconnectingShort
        : IsControllable ? Strings.TryAgain
        : Strings.Unavailable
        : null;
    public bool HasCompactStatus => !string.IsNullOrWhiteSpace(CompactStatusText);

    public bool IsRenaming
    {
        get => _isRenaming;
        set
        {
            if (_isRenaming == value)
            {
                return;
            }

            _isRenaming = value;
            OnPropertyChanged();
        }
    }

    public string EditName
    {
        get => _editName;
        set
        {
            if (string.Equals(_editName, value, StringComparison.Ordinal))
            {
                return;
            }

            _editName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSaveName));
        }
    }

    public bool CanSaveName => !string.IsNullOrWhiteSpace(EditName);

    public bool IsBeingDragged
    {
        get => _isBeingDragged;
        set
        {
            if (_isBeingDragged == value)
            {
                return;
            }

            _isBeingDragged = value;
            OnPropertyChanged();
        }
    }

    public string ToolTip
    {
        get
        {
            var serial = string.IsNullOrWhiteSpace(Snapshot.Serial) ? Strings.Unknown : Snapshot.Serial;
            var backend = Snapshot.Status == MonitorSupportStatus.Reconnecting ? Strings.Reconnecting : Snapshot.Backend switch
            {
                BrightnessBackendKind.DdcHighLevel => "DDC/CI",
                BrightnessBackendKind.DdcVcp => "DDC/CI VCP 0x10",
                BrightnessBackendKind.WmiInternal => Strings.WindowsBuiltInControl,
                _ => Strings.Unavailable
            };

            var details = $"{Snapshot.FriendlyName}\n{Strings.Serial}: {serial}\n{Snapshot.DisplayDevice}\n{Strings.Control}: {backend}";
            return HasStatus ? $"{details}\n{StatusText}" : details;
        }
    }

    public int CurrentPercent
    {
        get => _currentPercent;
        set
        {
            var bounded = Math.Clamp(value, 0, 100);
            if (_currentPercent == bounded)
            {
                return;
            }

            _currentPercent = bounded;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BrightnessText));

            if (!_suppressWrite && IsControllable && !_disposed)
            {
                StatusText = null;
                _writeCoordinator!.Queue(bounded);
                UserAdjusted?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public string BrightnessText => IsControllable ? Strings.Percent(CurrentPercent) : "—";

    public string? StatusText
    {
        get => _statusText;
        private set
        {
            if (string.Equals(_statusText, value, StringComparison.Ordinal))
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasStatus));
            OnPropertyChanged(nameof(CompactStatusText));
            OnPropertyChanged(nameof(HasCompactStatus));
            OnPropertyChanged(nameof(ToolTip));
        }
    }

    /// <summary>
    /// Confirms the value the user chose on the tile. Counts as a change by the user even when the value is the one
    /// already shown, because it replaces any value still being written, such as a profile's.
    /// </summary>
    public Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (IsControllable && !_disposed)
        {
            UserAdjusted?.Invoke(this, EventArgs.Empty);
        }

        return FlushAsync(cancellationToken);
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!IsControllable || _writeCoordinator is null || _disposed)
        {
            return;
        }

        try
        {
            await _writeCoordinator.FlushAsync(CurrentPercent, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailed(exception);
        }
    }

    /// <summary>
    /// Sets a brightness chosen outside the tile, such as a profile's, through the same queue as the tile's own
    /// writes, so it replaces any write still pending from the tile instead of racing it.
    /// </summary>
    /// <returns>The brightness the display reported, or null when newer input on the tile replaced it.</returns>
    /// <remarks>A failure is shown on the tile as well as thrown.</remarks>
    public async Task<int?> SetBrightnessAsync(int percent, CancellationToken cancellationToken = default)
    {
        if (!IsControllable || _writeCoordinator is null || _disposed)
        {
            throw new InvalidOperationException(StatusText ?? Strings.DisplayCannotBeControlled);
        }

        try
        {
            return await _writeCoordinator.FlushAsync(percent, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            HandleFailed(exception);
            throw;
        }
    }

    private async Task<int> WriteBrightnessAsync(
        int percent,
        BrightnessWriteMode mode,
        CancellationToken cancellationToken)
    {
        if (_brightnessWriter is not null)
        {
            return await _brightnessWriter(this, percent, mode, cancellationToken).ConfigureAwait(false);
        }

        var result = await _monitorService.SetBrightnessAsync(Snapshot.Id, percent, mode, cancellationToken)
            .ConfigureAwait(false);
        Snapshot = result.Snapshot with { DisplayName = Snapshot.DisplayName };
        return result.ActualPercent;
    }

    private void HandleApplied(int actualPercent, long version)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            _appliedPercent = Math.Clamp(actualPercent, 0, 100);

            // The user may have moved on since this write started; the tile keeps showing their newer value,
            // which its own write will confirm.
            if (!_writeCoordinator!.IsLatest(version))
            {
                return;
            }

            _suppressWrite = true;
            try
            {
                CurrentPercent = _appliedPercent;
                StatusText = null;
            }
            finally
            {
                _suppressWrite = false;
            }
        });
    }

    /// <summary>
    /// Applies a fresh discovery result for the same display without rebuilding the row. Must be called on the
    /// dispatcher thread.
    /// </summary>
    public void UpdateSnapshot(MonitorSnapshot snapshot)
    {
        if (_disposed)
        {
            return;
        }

        if (Snapshot == snapshot && _currentPercent == snapshot.CurrentPercent &&
            _appliedPercent == snapshot.CurrentPercent && _statusText == snapshot.ErrorMessage)
        {
            return;
        }

        var previous = (DisplayName, IsControllable, CurrentPercent, BrightnessText, StatusText,
            HasStatus, CompactStatusText, HasCompactStatus, ToolTip);
        Snapshot = snapshot;
        // A discovery updates the backing values directly: it must never queue a brightness write.
        _appliedPercent = snapshot.CurrentPercent;
        _currentPercent = snapshot.CurrentPercent;
        _statusText = snapshot.ErrorMessage;

        if (previous.DisplayName != DisplayName) OnPropertyChanged(nameof(DisplayName));
        if (previous.IsControllable != IsControllable) OnPropertyChanged(nameof(IsControllable));
        if (previous.CurrentPercent != CurrentPercent) OnPropertyChanged(nameof(CurrentPercent));
        if (previous.BrightnessText != BrightnessText) OnPropertyChanged(nameof(BrightnessText));
        if (previous.StatusText != StatusText) OnPropertyChanged(nameof(StatusText));
        if (previous.HasStatus != HasStatus) OnPropertyChanged(nameof(HasStatus));
        if (previous.CompactStatusText != CompactStatusText) OnPropertyChanged(nameof(CompactStatusText));
        if (previous.HasCompactStatus != HasCompactStatus) OnPropertyChanged(nameof(HasCompactStatus));
        if (previous.ToolTip != ToolTip) OnPropertyChanged(nameof(ToolTip));
    }

    public void SetDisplayName(string displayName)
    {
        if (DisplayName == displayName) return;
        Snapshot = Snapshot with { DisplayName = displayName };
        OnPropertyChanged(nameof(DisplayName));
    }

    private void HandleFailed(Exception exception)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                StatusText = exception is InvalidOperationException
                    ? exception.Message
                    : Strings.CouldNotSetBrightness;
            }
        });
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_writeCoordinator is not null)
        {
            _writeCoordinator.Applied -= HandleApplied;
            _writeCoordinator.Failed -= HandleFailed;
            await _writeCoordinator.DisposeAsync().ConfigureAwait(false);
        }
    }
}
