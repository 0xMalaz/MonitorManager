using MonitorCenter.Models;

namespace MonitorCenter.Services;

internal sealed class BrightnessWriteCoordinator : IAsyncDisposable
{
    private readonly Func<int, BrightnessWriteMode, CancellationToken, Task<int>> _writer;
    private readonly TimeSpan _debounceDelay;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();
    private readonly HashSet<Task> _backgroundTasks = [];
    private CancellationTokenSource? _debounceCancellation;
    private int? _lastPreviewPercent;
    private bool _disposed;

    public BrightnessWriteCoordinator(
        Func<int, BrightnessWriteMode, CancellationToken, Task<int>> writer,
        TimeSpan? debounceDelay = null)
    {
        _writer = writer;
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(75);
    }

    public event Action<int>? Applied;
    public event Action<Exception>? Failed;

    public void Queue(int percent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bounded = Math.Clamp(percent, 0, 100);
        CancellationTokenSource cancellation;

        lock (_sync)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            _debounceCancellation = cancellation;
        }

        Track(ApplyAfterDelayAsync(bounded, cancellation.Token));
    }

    public async Task<int> FlushAsync(int percent, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        lock (_sync)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = null;
        }

        return await ApplyAsync(Math.Clamp(percent, 0, 100), isPreview: false, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyAfterDelayAsync(int percent, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounceDelay, cancellationToken).ConfigureAwait(false);
            await ApplyAsync(percent, isPreview: true, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
    }

    private async Task<int> ApplyAsync(int percent, bool isPreview, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // Debounced writes during an interaction skip the read-back. The final write only re-reads the
            // display when the last preview already sent the same value, so it is not written twice.
            BrightnessWriteMode mode;
            if (isPreview)
            {
                mode = BrightnessWriteMode.Preview;
            }
            else
            {
                mode = _lastPreviewPercent == percent ? BrightnessWriteMode.Verify : BrightnessWriteMode.Commit;
                _lastPreviewPercent = null;
            }

            var actual = await _writer(percent, mode, cancellationToken).ConfigureAwait(false);
            if (isPreview)
            {
                _lastPreviewPercent = percent;
            }

            Applied?.Invoke(actual);
            return actual;
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private void Track(Task task)
    {
        lock (_sync)
        {
            _backgroundTasks.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                lock (_sync)
                {
                    _backgroundTasks.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Task[] pending;

        lock (_sync)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = null;
            pending = [.. _backgroundTasks];
        }

        try
        {
            await Task.WhenAll(pending).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        await _writeGate.WaitAsync().ConfigureAwait(false);
        _writeGate.Release();
        _writeGate.Dispose();
    }
}
