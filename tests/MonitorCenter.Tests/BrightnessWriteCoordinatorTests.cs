using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class BrightnessWriteCoordinatorTests
{
    [TestMethod]
    public async Task Queue_CoalescesRapidChangesAndFlushPreservesFinalValue()
    {
        var writes = new ConcurrentQueue<(int Value, BrightnessWriteMode Mode)>();
        var appliedThirty = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var coordinator = new BrightnessWriteCoordinator(
            (value, mode, _) =>
            {
                writes.Enqueue((value, mode));
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

        CollectionAssert.AreEqual(new[] { (30, BrightnessWriteMode.Preview) }, writes.ToArray());

        var actual = await coordinator.FlushAsync(55);

        Assert.AreEqual(55, actual);
        CollectionAssert.AreEqual(
            new[] { (30, BrightnessWriteMode.Preview), (55, BrightnessWriteMode.Commit) },
            writes.ToArray());
    }

    [TestMethod]
    public async Task Flush_AfterPreviewOfSameValueOnlyVerifies()
    {
        var writes = new ConcurrentQueue<(int Value, BrightnessWriteMode Mode)>();
        var appliedForty = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        await using var coordinator = new BrightnessWriteCoordinator(
            (value, mode, _) =>
            {
                writes.Enqueue((value, mode));
                return Task.FromResult(value);
            },
            TimeSpan.FromMilliseconds(25));
        coordinator.Applied += value =>
        {
            if (value == 40)
            {
                appliedForty.TrySetResult();
            }
        };

        coordinator.Queue(40);
        await appliedForty.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await coordinator.FlushAsync(40);
        await coordinator.FlushAsync(40);

        CollectionAssert.AreEqual(
            new[]
            {
                (40, BrightnessWriteMode.Preview),
                (40, BrightnessWriteMode.Verify),
                (40, BrightnessWriteMode.Commit)
            },
            writes.ToArray());
    }
}
