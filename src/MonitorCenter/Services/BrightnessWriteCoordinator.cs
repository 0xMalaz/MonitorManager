using MonitorCenter.Models;

namespace MonitorCenter.Services;

/// <summary>
/// Serializes the brightness writes of one display. Every request gets a newer version than the one before it; a
/// request that a newer one has replaced is skipped if it has not started yet, and <see cref="Applied"/> reports
/// the version of each completed write, so a result that arrives after newer input can be told apart with
/// <see cref="IsLatest"/> and not shown over it.
/// </summary>
internal sealed class BrightnessWriteCoordinator : IAsyncDisposable
{
    private readonly Func<int, BrightnessWriteMode, CancellationToken, Task<int>> _writer;
    private readonly TimeSpan _debounceDelay;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();
    private readonly HashSet<Task> _backgroundTasks = [];
    private CancellationTokenSource? _debounceCancellation;
    private int? _lastPreviewPercent;
    private long _version;
    private bool _disposed;

    public BrightnessWriteCoordinator(
        Func<int, BrightnessWriteMode, CancellationToken, Task<int>> writer,
        TimeSpan? debounceDelay = null)
    {
        _writer = writer;
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(75);
    }

    /// <summary>Raised with the brightness a display reported and the version of the request that set it.</summary>
    public event Action<int, long>? Applied;
    public event Action<Exception>? Failed;

    /// <summary>True when no request newer than <paramref name="version"/> has been made.</summary>
    public bool IsLatest(long version) => Interlocked.Read(ref _version) == version;

    public void Queue(int percent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var bounded = Math.Clamp(percent, 0, 100);
        CancellationTokenSource cancellation;
        long version;

        lock (_sync)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            cancellation = new CancellationTokenSource();
            _debounceCancellation = cancellation;
            version = Interlocked.Increment(ref _version);
        }

        Track(ApplyAfterDelayAsync(bounded, version, cancellation.Token));
    }

    /// <summary>Writes and confirms a value straight away, replacing any queued value.</summary>
    /// <returns>The brightness the display reported, or null when a newer request replaced this one first.</returns>
    public async Task<int?> FlushAsync(int percent, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long version;

        lock (_sync)
        {
            _debounceCancellation?.Cancel();
            _debounceCancellation?.Dispose();
            _debounceCancellation = null;
            version = Interlocked.Increment(ref _version);
        }

        return await ApplyAsync(Math.Clamp(percent, 0, 100), isPreview: false, version, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task ApplyAfterDelayAsync(int percent, long version, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(_debounceDelay, cancellationToken).ConfigureAwait(false);
            await ApplyAsync(percent, isPreview: true, version, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Failed?.Invoke(exception);
        }
    }

    private async Task<int?> ApplyAsync(int percent, bool isPreview, long version, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // A request replaced while it waited is not sent: the newer one carries the value the user wants.
            if (!IsLatest(version))
            {
                return null;
            }

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

            Applied?.Invoke(actual, version);
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
