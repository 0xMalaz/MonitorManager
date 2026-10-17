using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class UpdateCoordinatorTests
{
    [TestMethod]
    public async Task Run_WithoutUpdate_ChecksThenSaysUpToDate()
    {
        var updater = new FakeUpdater();
        var shutdowns = 0;
        using var coordinator = new UpdateCoordinator(updater, () => shutdowns++);
        var stages = RecordStages(coordinator);

        await coordinator.RunAsync();

        CollectionAssert.AreEqual(new[] { UpdateStage.Checking, UpdateStage.NoUpdate }, stages);
        Assert.AreEqual(UpdateCoordinator.UpToDateMessage, coordinator.Status.Message);
        Assert.AreEqual(0, updater.ApplyCount);
        Assert.AreEqual(0, shutdowns);
    }

    [TestMethod]
    public async Task Run_WithUpdate_ReportsDownloadProgressThenRestartsIntoIt()
    {
        var sawProgress = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var updater = new FakeUpdater { AvailableVersion = "1.1.0", ProgressSteps = [40], DownloadGate = sawProgress.Task };
        var shutdowns = 0;
        using var coordinator = new UpdateCoordinator(updater, () => shutdowns++);
        var statuses = new List<UpdateStatus>();
        coordinator.StatusChanged += status =>
        {
            lock (statuses) statuses.Add(status);
            if (status.Progress == 40) sawProgress.TrySetResult();
        };

        await coordinator.RunAsync();

        Assert.AreEqual(UpdateStage.Checking, statuses[0].Stage);
        Assert.AreEqual("Downloading 1.1.0…", statuses[1].Message);
        Assert.IsTrue(statuses.Any(status => status is { Stage: UpdateStage.Downloading, Progress: 40 }));
        Assert.IsTrue(statuses.All(status => status.Progress is >= 0 and <= 100));
        Assert.AreEqual(UpdateStage.Restarting, statuses[^1].Stage);
        Assert.AreEqual(1, updater.DownloadCount);
        Assert.AreEqual(1, updater.ApplyCount);
        Assert.AreEqual(1, shutdowns);
    }

    [TestMethod]
    public async Task Run_WhenNotInstalled_ExplainsAndDoesNotContactGitHub()
    {
        var updater = new FakeUpdater { CanUpdate = false };
        using var coordinator = new UpdateCoordinator(updater, () => { });

        await coordinator.RunAsync();

        Assert.AreEqual(UpdateStage.Unavailable, coordinator.Status.Stage);
        Assert.AreEqual(UpdateCoordinator.UnavailableMessage, coordinator.Status.Message);
        Assert.AreEqual(0, updater.CheckCount);
    }

    [TestMethod]
    public async Task Run_WhenCheckFails_ReportsFailureAndCanRetry()
    {
        var updater = new FakeUpdater { CheckFailure = new HttpRequestException("offline") };
        using var coordinator = new UpdateCoordinator(updater, () => { });

        await coordinator.RunAsync();

        Assert.AreEqual(UpdateStage.Failed, coordinator.Status.Stage);
        StringAssert.Contains(coordinator.Status.Message, "offline");
        Assert.AreEqual(0, updater.ApplyCount);

        updater.CheckFailure = null;
        await coordinator.RunAsync();

        Assert.AreEqual(2, updater.CheckCount);
        Assert.AreEqual(UpdateStage.NoUpdate, coordinator.Status.Stage);
    }

    [TestMethod]
    public async Task Coordinator_NeverChecksUntilTheUserAsks()
    {
        var updater = new FakeUpdater { AvailableVersion = "1.1.0" };
        using var coordinator = new UpdateCoordinator(updater, () => { });

        await Task.Yield();

        Assert.AreEqual(0, updater.CheckCount);
        Assert.AreEqual(0, updater.DownloadCount);
        Assert.AreEqual(UpdateStage.Idle, coordinator.Status.Stage);
    }

    [TestMethod]
    public async Task Run_WithDownloadedUpdate_RestartsWithoutCheckingAgain()
    {
        var updater = new FakeUpdater { PendingVersion = "1.1.0" };
        var shutdowns = 0;
        using var coordinator = new UpdateCoordinator(updater, () => shutdowns++);

        await coordinator.RunAsync();

        Assert.AreEqual(0, updater.CheckCount);
        Assert.AreEqual(1, updater.ApplyCount);
        Assert.AreEqual(1, shutdowns);
    }

    [TestMethod]
    public async Task Run_WhileCheckIsRunning_DoesNotStartAnother()
    {
        var release = new TaskCompletionSource();
        var updater = new FakeUpdater { CheckGate = release.Task };
        using var coordinator = new UpdateCoordinator(updater, () => { });

        var first = coordinator.RunAsync();
        var second = coordinator.RunAsync();
        Assert.IsTrue(second.IsCompleted);
        Assert.AreEqual(UpdateStage.Checking, coordinator.Status.Stage);
        release.SetResult();
        await first;

        Assert.AreEqual(1, updater.CheckCount);
    }

    [TestMethod]
    public async Task Dispose_WhileChecking_StopsWithoutReportingFailure()
    {
        var release = new TaskCompletionSource();
        var updater = new FakeUpdater { AvailableVersion = "1.1.0", CheckGate = release.Task };
        var coordinator = new UpdateCoordinator(updater, () => { });

        var run = coordinator.RunAsync();
        coordinator.Dispose();
        release.SetResult();
        await run;

        Assert.AreEqual(UpdateStage.Checking, coordinator.Status.Stage);
        Assert.AreEqual(0, updater.DownloadCount);
    }

    private static List<UpdateStage> RecordStages(UpdateCoordinator coordinator)
    {
        var stages = new List<UpdateStage>();
        coordinator.StatusChanged += status => stages.Add(status.Stage);
        return stages;
    }

    private sealed class FakeUpdater : IAppUpdater
    {
        public bool CanUpdate { get; set; } = true;
        public string? PendingVersion { get; set; }
        public string? AvailableVersion { get; set; }
        public Exception? CheckFailure { get; set; }
        public Task CheckGate { get; set; } = Task.CompletedTask;
        public int[] ProgressSteps { get; set; } = [];
        public Task DownloadGate { get; set; } = Task.CompletedTask;
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public int ApplyCount { get; private set; }

        public async Task<string?> CheckAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            await CheckGate;
            if (CheckFailure is not null) throw CheckFailure;
            return AvailableVersion;
        }

        public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
        {
            DownloadCount++;
            foreach (var percent in ProgressSteps) progress.Report(percent);
            await DownloadGate;
            PendingVersion = AvailableVersion;
        }

        public void ApplyAfterExit() => ApplyCount++;
    }
}
