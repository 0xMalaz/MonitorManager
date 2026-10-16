using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;
using MonitorCenter.Services;
using MonitorCenter.UI;

namespace MonitorCenter.Tests;

[TestClass]
[DoNotParallelize]
public sealed class MonitorRowViewModelTests
{
    [TestMethod]
    public void IdenticalSnapshots_DoNotNotifyBindingsOrWriteBrightness() => RunOnDispatcher(async dispatcher =>
    {
        var writes = 0;
        await using var service = new MonitorService();
        await using var row = new MonitorRowViewModel(Snapshot(), service, dispatcher,
            (_, value, _, _) => { writes++; return Task.FromResult(value); });
        var properties = new List<string?>();
        row.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        for (var index = 0; index < 100; index++) row.UpdateSnapshot(Snapshot());
        row.SetDisplayName(row.DisplayName);
        Assert.AreEqual(0, properties.Count);
        Assert.AreEqual(0, writes);
    });

    [TestMethod]
    public void ChangedSnapshot_NotifiesOnlyChangedValuesWithoutWriting() => RunOnDispatcher(async dispatcher =>
    {
        var writes = 0;
        var adjustments = 0;
        await using var service = new MonitorService();
        await using var row = new MonitorRowViewModel(Snapshot(), service, dispatcher,
            (_, value, _, _) => { writes++; return Task.FromResult(value); });
        var properties = new List<string?>();
        row.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        row.UserAdjusted += (_, _) => adjustments++;

        row.UpdateSnapshot(Snapshot() with { CurrentRaw = 75 });
        CollectionAssert.AreEquivalent(new[] { "CurrentPercent", "BrightnessText" }, properties);
        Assert.AreEqual(75, row.CurrentPercent);
        Assert.AreEqual(75, row.AppliedPercent);
        properties.Clear();

        row.UpdateSnapshot(row.Snapshot with { DisplayName = "Desk", Backend = BrightnessBackendKind.DdcVcp });
        CollectionAssert.AreEquivalent(new[] { "DisplayName", "ToolTip" }, properties);
        StringAssert.Contains(row.ToolTip, "VCP");
        Assert.AreEqual(0, writes);
        Assert.AreEqual(0, adjustments);
    });

    [TestMethod]
    public void StatusChanges_RefreshDerivedTextEvenWhenErrorMessageIsUnchanged() => RunOnDispatcher(async dispatcher =>
    {
        var unavailable = Snapshot() with
        {
            Status = MonitorSupportStatus.Unavailable,
            Backend = BrightnessBackendKind.None,
            ErrorMessage = "Waiting for display"
        };
        await using var service = new MonitorService();
        await using var row = new MonitorRowViewModel(unavailable, service, dispatcher);
        var properties = new List<string?>();
        row.PropertyChanged += (_, e) => properties.Add(e.PropertyName);
        row.UpdateSnapshot(unavailable with { Status = MonitorSupportStatus.Reconnecting });
        CollectionAssert.AreEquivalent(new[] { "CompactStatusText", "ToolTip" }, properties);
        Assert.AreEqual("Reconnecting…", row.CompactStatusText);

        properties.Clear();
        row.UpdateSnapshot(unavailable with { ErrorMessage = null });
        CollectionAssert.Contains(properties, "HasStatus");
        CollectionAssert.Contains(properties, "HasCompactStatus");
        Assert.IsFalse(row.HasStatus);
        Assert.IsFalse(row.HasCompactStatus);
    });

    [TestMethod]
    public void LateWriteResult_DoesNotOverrideNewerInput() => RunOnDispatcher(async dispatcher =>
    {
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<int>();
        await using var monitorService = new MonitorService();
        await using var row = new MonitorRowViewModel(
            Snapshot(),
            monitorService,
            dispatcher,
            async (_, value, _, _) =>
            {
                lock (writes)
                {
                    writes.Add(value);
                }

                if (value == 51)
                {
                    await releaseFirstWrite.Task;
                }

                return value;
            });

        // Two quick key presses: the first write is still running when the second value is chosen.
        row.CurrentPercent = 51;
        var first = row.FlushAsync();
        row.CurrentPercent = 52;
        var second = row.FlushAsync();

        releaseFirstWrite.SetResult();
        await first;
        await WaitForIdleAsync(dispatcher);
        Assert.AreEqual(52, row.CurrentPercent, "The late result for 51 replaced the newer value.");

        await second;
        await WaitForIdleAsync(dispatcher);
        Assert.AreEqual(52, row.CurrentPercent);
        Assert.AreEqual(52, row.AppliedPercent);
        CollectionAssert.AreEqual(new[] { 51, 52 }, writes);
    });

    [TestMethod]
    public void SetBrightness_RaisesNoUserAdjustmentAndUpdatesTheTile() => RunOnDispatcher(async dispatcher =>
    {
        await using var monitorService = new MonitorService();
        await using var row = new MonitorRowViewModel(
            Snapshot(),
            monitorService,
            dispatcher,
            (_, value, _, _) => Task.FromResult(value));
        var adjustments = 0;
        row.UserAdjusted += (_, _) => adjustments++;

        Assert.AreEqual(30, await row.SetBrightnessAsync(30));
        await WaitForIdleAsync(dispatcher);
        Assert.AreEqual(30, row.CurrentPercent);
        Assert.AreEqual(0, adjustments);

        row.CurrentPercent = 40;
        Assert.AreEqual(1, adjustments);
    });

    private static Task WaitForIdleAsync(Dispatcher dispatcher) =>
        dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static MonitorSnapshot Snapshot() => new(
        "monitor-1",
        "Monitor",
        "Monitor",
        null,
        @"\\.\DISPLAY1",
        new DesktopBounds(0, 0, 1920, 1080),
        0,
        100,
        50,
        BrightnessBackendKind.DdcHighLevel,
        MonitorSupportStatus.Ready,
        null);

    private static void RunOnDispatcher(Func<Dispatcher, Task> body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await body(dispatcher);
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
                finally
                {
                    frame.Continue = false;
                }
            }));
            Dispatcher.PushFrame(frame);
            dispatcher.InvokeShutdown();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Dispatcher test timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
