using MonitorCenter.Models;

namespace MonitorCenter.Services;

/// <summary>
/// Asks displays that did not answer DDC/CI again a few times after a refresh, because most of them answer again
/// within seconds, for example while waking up or after a dropped reply.
/// </summary>
internal sealed class DisplayRecoveryScheduler : IDisposable
{
    internal const string ReconnectingMessage = "Reconnecting to the display…";

    /// <summary>When each recheck runs, counted from the refresh that left a display unavailable.</summary>
    internal static readonly TimeSpan[] RecheckTimes =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(10)
    ];

    private readonly Func<bool, CancellationToken, Task<bool>> _recheck;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly CancellationTokenSource _lifetime = new();
    private int _isRunning;
    private bool _disposed;

    /// <param name="recheck">
    /// Rechecks the displays. Its argument is true for the last recheck; it returns true when no display needs
    /// recovering any more.
    /// </param>
    public DisplayRecoveryScheduler(
        Func<bool, CancellationToken, Task<bool>> recheck,
        Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        _recheck = recheck;
        _delay = delay ?? Task.Delay;
    }

    public bool IsRunning => Volatile.Read(ref _isRunning) != 0;

    /// <summary>Starts the rechecks, unless they are already running.</summary>
    public Task StartAsync()
    {
        if (_disposed || Interlocked.Exchange(ref _isRunning, 1) != 0)
        {
            return Task.CompletedTask;
        }

        return RunAsync(_lifetime.Token);
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            var elapsed = TimeSpan.Zero;
            for (var index = 0; index < RecheckTimes.Length; index++)
            {
                await _delay(RecheckTimes[index] - elapsed, cancellationToken).ConfigureAwait(false);
                elapsed = RecheckTimes[index];
                if (await _recheck(index == RecheckTimes.Length - 1, cancellationToken).ConfigureAwait(false))
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
        {
            // The app is shutting down.
        }
        finally
        {
            Volatile.Write(ref _isRunning, 0);
        }
    }

    /// <summary>
    /// Shows displays that worked earlier and stopped answering as reconnecting rather than unavailable while
    /// rechecks are still to come.
    /// </summary>
    public static IReadOnlyList<MonitorSnapshot> MarkReconnecting(
        IReadOnlyList<MonitorSnapshot> snapshots,
        IReadOnlySet<string> previouslyControllableIds,
        bool moreRechecksToCome)
    {
        if (!moreRechecksToCome)
        {
            return snapshots;
        }

        return snapshots
            .Select(snapshot => snapshot.Status == MonitorSupportStatus.Unavailable &&
                                previouslyControllableIds.Contains(snapshot.Id)
                ? snapshot with { Status = MonitorSupportStatus.Reconnecting, ErrorMessage = ReconnectingMessage }
                : snapshot)
            .ToArray();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
