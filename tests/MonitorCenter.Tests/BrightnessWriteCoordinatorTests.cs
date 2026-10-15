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
        coordinator.Applied += (value, _) =>
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
        coordinator.Applied += (value, _) =>
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

    [TestMethod]
    public async Task Flush_SkipsRequestsReplacedWhileWaitingAndMarksLateResultsStale()
    {
        var writes = new ConcurrentQueue<int>();
        var firstWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var applied = new ConcurrentQueue<(int Value, bool WasLatest)>();
        BrightnessWriteCoordinator? coordinator = null;

        await using (coordinator = new BrightnessWriteCoordinator(
            async (value, _, _) =>
            {
                writes.Enqueue(value);
                if (value == 51)
                {
                    firstWriteStarted.TrySetResult();
                    await releaseFirstWrite.Task;
                }

                return value;
            }))
        {
            coordinator.Applied += (value, version) => applied.Enqueue((value, coordinator.IsLatest(version)));

            var first = coordinator.FlushAsync(51);
            await firstWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
            var second = coordinator.FlushAsync(52);
            var third = coordinator.FlushAsync(53);
            releaseFirstWrite.SetResult();

            Assert.AreEqual(51, await first);
            Assert.IsNull(await second);
            Assert.AreEqual(53, await third);
        }

        CollectionAssert.AreEqual(new[] { 51, 53 }, writes.ToArray());
        CollectionAssert.AreEqual(new[] { (51, false), (53, true) }, applied.ToArray());
    }
}
