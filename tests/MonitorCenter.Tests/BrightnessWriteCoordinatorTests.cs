using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class BrightnessWriteCoordinatorTests
{
    [TestMethod]
    public async Task Queue_CoalescesRapidChangesAndFlushPreservesFinalValue()
    {
        var writes = new ConcurrentQueue<int>();
        var appliedThirty = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var coordinator = new BrightnessWriteCoordinator(
            (value, _) =>
            {
                writes.Enqueue(value);
                return Task.FromResult(value);
            },
            TimeSpan.FromMilliseconds(25));
        coordinator.Applied += value =>
        {
            if (value == 30)
            {
                appliedThirty.TrySetResult();
            }
        };

        coordinator.Queue(10);
        coordinator.Queue(20);
        coordinator.Queue(30);
        await appliedThirty.Task.WaitAsync(TimeSpan.FromSeconds(2));

        CollectionAssert.AreEqual(new[] { 30 }, writes.ToArray());

        var actual = await coordinator.FlushAsync(55);

        Assert.AreEqual(55, actual);
        CollectionAssert.AreEqual(new[] { 30, 55 }, writes.ToArray());
    }
}
