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
    private readonly Func<MonitorRowViewModel, int, CancellationToken, Task<int>>? _brightnessWriter;
    private readonly BrightnessWriteCoordinator? _writeCoordinator;
    private int _currentPercent;
    private int _appliedPercent;
    private string? _statusText;
    private bool _suppressWrite;
    private bool _isBeingDragged;
    private bool _disposed;

    public MonitorRowViewModel(
        MonitorSnapshot snapshot,
        MonitorService monitorService,
        Dispatcher dispatcher,
        Func<MonitorRowViewModel, int, CancellationToken, Task<int>>? brightnessWriter = null)
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

    public MonitorSnapshot Snapshot { get; private set; }
    public int AppliedPercent => _appliedPercent;
    public string DisplayName => Snapshot.DisplayName;
    public bool IsControllable => Snapshot.IsControllable;
    public bool HasStatus => !string.IsNullOrWhiteSpace(StatusText);
    public string? CompactStatusText => HasStatus
        ? IsControllable ? "Try again" : "Unavailable"
        : null;
    public bool HasCompactStatus => !string.IsNullOrWhiteSpace(CompactStatusText);

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
            var serial = string.IsNullOrWhiteSpace(Snapshot.Serial) ? "Unknown" : Snapshot.Serial;
            var backend = Snapshot.Backend switch
            {
                BrightnessBackendKind.DdcHighLevel => "DDC/CI",
                BrightnessBackendKind.DdcVcp => "DDC/CI VCP 0x10",
                BrightnessBackendKind.WmiInternal => "Windows built-in display control",
                _ => "Unavailable"
            };

            var details = $"{Snapshot.FriendlyName}\nSerial: {serial}\n{Snapshot.DisplayDevice}\nControl: {backend}";
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
            }
        }
    }

    public string BrightnessText => IsControllable ? $"{CurrentPercent}%" : "—";

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

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (!IsControllable || _writeCoordinator is null || _disposed)
        {
            return;
        }

        try
        {
            var actual = await _writeCoordinator
                .FlushAsync(CurrentPercent, cancellationToken)
                .ConfigureAwait(false);
            HandleApplied(actual);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            HandleFailed(exception);
        }
    }

    private async Task<int> WriteBrightnessAsync(int percent, CancellationToken cancellationToken)
    {
        if (_brightnessWriter is not null)
        {
            return await _brightnessWriter(this, percent, cancellationToken).ConfigureAwait(false);
        }

        var result = await _monitorService.SetBrightnessAsync(Snapshot.Id, percent, cancellationToken)
            .ConfigureAwait(false);
        Snapshot = result.Snapshot with { DisplayName = Snapshot.DisplayName };
        return result.ActualPercent;
    }

    private void HandleApplied(int actualPercent)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            _suppressWrite = true;
            try
            {
                CurrentPercent = actualPercent;
                _appliedPercent = actualPercent;
                StatusText = null;
            }
            finally
            {
                _suppressWrite = false;
            }
        });
    }

    public void ApplyExternalBrightness(int actualPercent)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (_disposed)
            {
                return;
            }

            _suppressWrite = true;
            try
            {
                _appliedPercent = Math.Clamp(actualPercent, 0, 100);
                CurrentPercent = _appliedPercent;
                StatusText = null;
            }
            finally
            {
                _suppressWrite = false;
            }
        });
    }

    public void SetDisplayName(string displayName)
    {
        Snapshot = Snapshot with { DisplayName = displayName };
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(ToolTip));
    }

    private void HandleFailed(Exception exception)
    {
        _dispatcher.BeginInvoke(() =>
        {
            if (!_disposed)
            {
                StatusText = exception is InvalidOperationException
                    ? exception.Message
                    : "Could not set brightness. Use Refresh displays and try again.";
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
