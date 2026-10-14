using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class UpdateCoordinatorTests
{
    [TestMethod]
    public async Task CheckNow_WithoutUpdate_ShowsNoUpdatesThenReturnsToIdle()
    {
        var updater = new FakeUpdater();
        using var coordinator = new UpdateCoordinator(updater, NoDelay);
        var texts = RecordMenuText(coordinator);

        await coordinator.CheckNowAsync();

        CollectionAssert.AreEqual(
            new[] { UpdateCoordinator.CheckingText, UpdateCoordinator.NoUpdatesText, UpdateCoordinator.IdleText },
            texts.Distinct().ToArray());
        Assert.AreEqual(0, updater.ApplyCount);
    }

    [TestMethod]
    public async Task CheckNow_WithUpdate_DownloadsThenRestartsIntoIt()
    {
        var updater = new FakeUpdater { AvailableVersion = "1.1.0" };
        using var coordinator = new UpdateCoordinator(updater, NoDelay);
        var texts = RecordMenuText(coordinator);
        var restartRequested = false;
        coordinator.RestartRequested += (_, _) => restartRequested = true;

        await coordinator.CheckNowAsync();

        CollectionAssert.IsSubsetOf(
            new[] { UpdateCoordinator.CheckingText, UpdateCoordinator.UpdatingText, UpdateCoordinator.UpdatedText },
            texts);
        Assert.IsTrue(
            texts.IndexOf(UpdateCoordinator.UpdatingText) < texts.IndexOf(UpdateCoordinator.UpdatedText));
        Assert.AreEqual(1, updater.DownloadCount);
        Assert.AreEqual(1, updater.ApplyCount);
        Assert.IsTrue(restartRequested);
        Assert.AreEqual(UpdateCoordinator.UpdatedText, coordinator.MenuText);
    }

    [TestMethod]
    public async Task CheckNow_WhenNotInstalled_ExplainsAndDoesNotContactGitHub()
    {
        var updater = new FakeUpdater { CanUpdate = false };
        using var coordinator = new UpdateCoordinator(updater, NoDelay);
        var texts = RecordMenuText(coordinator);

        await coordinator.CheckNowAsync();

        CollectionAssert.Contains(texts, UpdateCoordinator.UnavailableText);
        Assert.AreEqual(0, updater.CheckCount);
        Assert.AreEqual(UpdateCoordinator.IdleText, coordinator.MenuText);
    }

    [TestMethod]
    public async Task CheckNow_WhenCheckFails_ShowsFailureThenReturnsToIdle()
    {
        var updater = new FakeUpdater { CheckFailure = new HttpRequestException("offline") };
        using var coordinator = new UpdateCoordinator(updater, NoDelay);
        var texts = RecordMenuText(coordinator);

        await coordinator.CheckNowAsync();

        CollectionAssert.Contains(texts, UpdateCoordinator.FailedText);
        Assert.AreEqual(UpdateCoordinator.IdleText, coordinator.MenuText);
        Assert.AreEqual(0, updater.ApplyCount);
    }

    [TestMethod]
    public async Task BackgroundCheck_DownloadsSilentlyAndOffersRestart()
    {
        var updater = new FakeUpdater { AvailableVersion = "1.1.0" };
        using var coordinator = new UpdateCoordinator(updater, NoDelay);
        var texts = RecordMenuText(coordinator);
        string? readyVersion = null;
        coordinator.UpdateReady += (_, version) => readyVersion = version;

        await coordinator.CheckInBackgroundAsync();

        Assert.AreEqual("1.1.0", readyVersion);
        Assert.AreEqual(0, updater.ApplyCount);
        Assert.IsFalse(texts.Contains(UpdateCoordinator.CheckingText));
        Assert.AreEqual("Restart to update to 1.1.0", coordinator.MenuText);
    }

    [TestMethod]
    public async Task CheckNow_WithDownloadedUpdate_RestartsWithoutCheckingAgain()
    {
        var updater = new FakeUpdater { PendingVersion = "1.1.0" };
        using var coordinator = new UpdateCoordinator(updater, NoDelay);

        await coordinator.CheckNowAsync();

        Assert.AreEqual(0, updater.CheckCount);
        Assert.AreEqual(1, updater.ApplyCount);
    }

    [TestMethod]
    public async Task CheckNow_WhileCheckIsRunning_JoinsItInsteadOfStartingAnother()
    {
        var release = new TaskCompletionSource();
        var updater = new FakeUpdater { CheckGate = release.Task };
        using var coordinator = new UpdateCoordinator(updater, NoDelay);

        var background = coordinator.CheckInBackgroundAsync();
        var manual = coordinator.CheckNowAsync();
        Assert.AreEqual(UpdateCoordinator.CheckingText, coordinator.MenuText);
        release.SetResult();
        await Task.WhenAll(background, manual);

        Assert.AreEqual(1, updater.CheckCount);
    }

    private static Task NoDelay(TimeSpan delay, CancellationToken cancellationToken) => Task.CompletedTask;

    private static List<string> RecordMenuText(UpdateCoordinator coordinator)
    {
        var texts = new List<string>();
        coordinator.PropertyChanged += (_, _) => texts.Add(coordinator.MenuText);
        return texts;
    }

    private sealed class FakeUpdater : IAppUpdater
    {
        public bool CanUpdate { get; set; } = true;
        public string? PendingVersion { get; set; }
        public string? AvailableVersion { get; set; }
        public Exception? CheckFailure { get; set; }
        public Task CheckGate { get; set; } = Task.CompletedTask;
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public int ApplyCount { get; private set; }

        public async Task<string?> CheckForUpdateAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            await CheckGate;
            if (CheckFailure is not null) throw CheckFailure;
            return AvailableVersion;
        }

        public Task DownloadUpdateAsync(IProgress<int> progress, CancellationToken cancellationToken)
        {
            DownloadCount++;
            PendingVersion = AvailableVersion;
            return Task.CompletedTask;
        }

        public void ApplyAfterExit() => ApplyCount++;
    }
}
