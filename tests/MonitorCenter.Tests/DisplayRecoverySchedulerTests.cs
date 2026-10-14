using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class DisplayRecoverySchedulerTests
{
    [TestMethod]
    public async Task Rechecks_AfterOneThreeAndTenSeconds_AndFlagsTheLastOne()
    {
        var elapsed = TimeSpan.Zero;
        var rechecks = new List<(TimeSpan At, bool IsLast)>();
        using var scheduler = new DisplayRecoveryScheduler(
            (isLast, _) =>
            {
                rechecks.Add((elapsed, isLast));
                return Task.FromResult(false);
            },
            (delay, _) =>
            {
                elapsed += delay;
                return Task.CompletedTask;
            });

        await scheduler.StartAsync();

        CollectionAssert.AreEqual(
            new[]
            {
                (TimeSpan.FromSeconds(1), false),
                (TimeSpan.FromSeconds(3), false),
                (TimeSpan.FromSeconds(10), true)
            },
            rechecks);
        Assert.IsFalse(scheduler.IsRunning);
    }

    [TestMethod]
    public async Task Rechecks_StopOnceEveryDisplayRecovered()
    {
        var count = 0;
        using var scheduler = new DisplayRecoveryScheduler((_, _) => Task.FromResult(++count == 2), NoDelay);

        await scheduler.StartAsync();

        Assert.AreEqual(2, count);
    }

    [TestMethod]
    public async Task Start_WhileRunning_DoesNotStartASecondRound()
    {
        var release = new TaskCompletionSource();
        var count = 0;
        using var scheduler = new DisplayRecoveryScheduler(
            async (_, _) =>
            {
                count++;
                await release.Task;
                return true;
            },
            NoDelay);

        var first = scheduler.StartAsync();
        var second = scheduler.StartAsync();
        Assert.IsTrue(scheduler.IsRunning);
        release.SetResult();
        await Task.WhenAll(first, second);

        Assert.AreEqual(1, count);
        Assert.IsFalse(scheduler.IsRunning);
    }

    [TestMethod]
    public async Task Dispose_CancelsPendingRechecks()
    {
        var count = 0;
        var scheduler = new DisplayRecoveryScheduler(
            (_, _) =>
            {
                count++;
                return Task.FromResult(false);
            },
            (_, token) => Task.Delay(Timeout.InfiniteTimeSpan, token));

        var running = scheduler.StartAsync();
        scheduler.Dispose();
        await running;

        Assert.AreEqual(0, count);
        Assert.IsFalse(scheduler.IsRunning);
    }

    [TestMethod]
    public void MarkReconnecting_OnlyAffectsDisplaysThatWorkedBefore()
    {
        var worked = Snapshot("worked", MonitorSupportStatus.Unavailable);
        var neverWorked = Snapshot("never", MonitorSupportStatus.Unavailable);
        var ready = Snapshot("ready", MonitorSupportStatus.Ready);
        var controllableIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "WORKED", "ready" };

        var marked = DisplayRecoveryScheduler.MarkReconnecting(
            [worked, neverWorked, ready],
            controllableIds,
            moreRechecksToCome: true);

        Assert.AreEqual(MonitorSupportStatus.Reconnecting, marked[0].Status);
        Assert.AreEqual(DisplayRecoveryScheduler.ReconnectingMessage, marked[0].ErrorMessage);
        Assert.IsFalse(marked[0].IsControllable);
        Assert.AreEqual(neverWorked, marked[1]);
        Assert.AreEqual(ready, marked[2]);
    }

    [TestMethod]
    public void MarkReconnecting_AfterTheLastRecheck_ReportsUnavailable()
    {
        var worked = Snapshot("worked", MonitorSupportStatus.Unavailable);

        var marked = DisplayRecoveryScheduler.MarkReconnecting(
            [worked],
            new HashSet<string> { "worked" },
            moreRechecksToCome: false);

        Assert.AreEqual(MonitorSupportStatus.Unavailable, marked[0].Status);
    }

    private static Task NoDelay(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;

    private static MonitorSnapshot Snapshot(string id, MonitorSupportStatus status) => new(
        id,
        id,
        id,
        null,
        @"\\.\DISPLAY1",
        new DesktopBounds(0, 0, 1920, 1080),
        0,
        100,
        50,
        status == MonitorSupportStatus.Ready ? BrightnessBackendKind.DdcHighLevel : BrightnessBackendKind.None,
        status,
        status == MonitorSupportStatus.Ready ? null : "Brightness unavailable — check DDC/CI and the display cable.");
}
