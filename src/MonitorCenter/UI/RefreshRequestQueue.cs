namespace MonitorCenter.UI;

/// <summary>
/// Combines requests received during a refresh into one trailing refresh. A trailing refresh is necessary because
/// a display may have changed after the running discovery began. All methods run on the flyout's dispatcher.
/// </summary>
internal sealed class RefreshRequestQueue(Func<bool, Task> refresh)
{
    private sealed class Request(bool reprobeKnownDisplays)
    {
        public bool ReprobeKnownDisplays { get; set; } = reprobeKnownDisplays;
        public TaskCompletionSource Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private Request? _pending;
    private bool _running;
    private bool _stopped;

    public Task RequestAsync(bool reprobeKnownDisplays)
    {
        if (_stopped)
        {
            return Task.CompletedTask;
        }

        if (_running)
        {
            _pending ??= new Request(reprobeKnownDisplays);
            // An explicit refresh must still reprobe even if automatic requests arrived first.
            _pending.ReprobeKnownDisplays |= reprobeKnownDisplays;
            return _pending.Completion.Task;
        }

        var request = new Request(reprobeKnownDisplays);
        _running = true;
        _ = RunAsync(request);
        return request.Completion.Task;
    }

    private async Task RunAsync(Request request)
    {
        while (true)
        {
            try
            {
                await refresh(request.ReprobeKnownDisplays);
                request.Completion.TrySetResult();
            }
            catch (Exception exception)
            {
                request.Completion.TrySetException(exception);
            }

            if (_pending is not { } next)
            {
                _running = false;
                return;
            }

            _pending = null;
            request = next;
        }
    }

    /// <summary>
    /// Stops accepting work and releases pending callers without starting their refresh. An already running
    /// discovery finishes normally; the monitor coordinator drains it before disposing its services.
    /// </summary>
    public void Stop()
    {
        _stopped = true;
        _pending?.Completion.TrySetResult();
        _pending = null;
    }
}
