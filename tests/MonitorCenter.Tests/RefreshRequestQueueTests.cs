using System.Runtime.ExceptionServices;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.UI;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class RefreshRequestQueueTests
{
    [TestMethod]
    public void BurstDuringDiscovery_RunsOnlyOneTrailingRefreshAndKeepsItsNewTopology() => RunOnDispatcher(async () =>
    {
        var release = Signal();
        var topology = 1;
        var observed = new List<int>();
        var queue = new RefreshRequestQueue(async _ =>
        {
            observed.Add(topology);
            if (observed.Count == 1) await release.Task;
        });

        var first = queue.RequestAsync(false);
        topology = 2;
        var burst = Enumerable.Range(0, 100).Select(_ => queue.RequestAsync(false)).ToArray();
        Assert.AreEqual(1, observed.Count);
        Assert.IsTrue(burst.All(task => ReferenceEquals(task, burst[0])));
        Assert.IsFalse(burst[0].IsCompleted);
        release.SetResult();
        await Task.WhenAll(burst.Append(first));

        CollectionAssert.AreEqual(new[] { 1, 2 }, observed);
    });

    [TestMethod]
    public void ManualRequest_UpgradesPendingAutomaticRefresh() => RunOnDispatcher(async () =>
    {
        var release = Signal();
        var requests = new List<bool>();
        var queue = new RefreshRequestQueue(async reprobe =>
        {
            requests.Add(reprobe);
            if (requests.Count == 1) await release.Task;
        });

        var first = queue.RequestAsync(false);
        var automatic = queue.RequestAsync(false);
        var manual = queue.RequestAsync(true);
        var laterAutomatic = queue.RequestAsync(false);
        release.SetResult();
        await Task.WhenAll(first, automatic, manual, laterAutomatic);
        CollectionAssert.AreEqual(new[] { false, true }, requests);
    });

    [TestMethod]
    public void EventDuringTrailingRefresh_IsNotLost() => RunOnDispatcher(async () =>
    {
        var releases = new[] { Signal(), Signal() };
        var secondStarted = Signal();
        var calls = 0;
        var queue = new RefreshRequestQueue(async _ =>
        {
            var index = calls++;
            if (index == 1) secondStarted.SetResult();
            if (index < releases.Length) await releases[index].Task;
        });

        var first = queue.RequestAsync(false);
        var second = queue.RequestAsync(false);
        releases[0].SetResult();
        await secondStarted.Task;
        var third = queue.RequestAsync(false);
        releases[1].SetResult();
        await Task.WhenAll(first, second, third);
        Assert.AreEqual(3, calls);
    });

    [TestMethod]
    public void Stop_DropsPendingWorkButLetsActiveDiscoveryDrain() => RunOnDispatcher(async () =>
    {
        var release = Signal();
        var calls = 0;
        var queue = new RefreshRequestQueue(async _ => { calls++; await release.Task; });
        var first = queue.RequestAsync(false);
        var pending = queue.RequestAsync(true);

        queue.Stop();
        queue.Stop();
        await pending;
        await queue.RequestAsync(true);
        Assert.IsFalse(first.IsCompleted);
        release.SetResult();
        await first;
        Assert.AreEqual(1, calls);
    });

    [TestMethod]
    public void FailedRefresh_ReportsFailureAndDoesNotStrandPendingOrFutureRequests() => RunOnDispatcher(async () =>
    {
        var release = Signal();
        var calls = 0;
        var queue = new RefreshRequestQueue(async _ =>
        {
            if (++calls == 1)
            {
                await release.Task;
                throw new InvalidOperationException("discovery failed");
            }
        });
        var first = queue.RequestAsync(false);
        var next = queue.RequestAsync(true);
        release.SetResult();
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => first);
        await next;
        await queue.RequestAsync(false);
        Assert.AreEqual(3, calls);
    });

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static void RunOnDispatcher(Func<Task> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try { await body(); }
                catch (Exception exception) { failure = exception; }
                finally { frame.Continue = false; }
            }));
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Refresh queue test timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
