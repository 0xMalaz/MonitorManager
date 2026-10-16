using System.IO;
using System.Collections.Specialized;
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
public sealed class ProfileEditingTests
{
    [TestMethod]
    public void ProfileChanges_KeepExistingTilesWithoutCollectionResets() => RunWithCoordinator(async (coordinator, _) =>
    {
        var first = new BrightnessProfile { Name = "First" };
        var second = new BrightnessProfile { Name = "Second" };
        coordinator.Profiles.Add(first);
        coordinator.Profiles.Add(second);
        var viewModel = new FlyoutViewModel(coordinator);
        var originalTiles = viewModel.ProfileTiles.ToArray();
        var changes = new List<NotifyCollectionChangedAction>();
        viewModel.ProfileTiles.CollectionChanged += (_, e) => changes.Add(e.Action);

        viewModel.StartProfileRename(originalTiles[1]);
        originalTiles[1].EditName = "Unfinished rename";
        await coordinator.RenameProfileAsync(first, "Work");
        Assert.AreEqual(0, changes.Count, "Renaming must not recreate any profile container.");
        Assert.AreSame(originalTiles[0], viewModel.ProfileTiles[0]);
        Assert.AreSame(originalTiles[1], viewModel.ProfileTiles[1]);
        Assert.AreEqual("Work", originalTiles[0].Name);
        Assert.IsTrue(originalTiles[1].IsRenaming);
        Assert.AreEqual("Unfinished rename", originalTiles[1].EditName);

        await coordinator.DeleteProfileAsync(first);
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Remove }, changes);
        Assert.AreSame(originalTiles[1], viewModel.ProfileTiles.Single());
        Assert.IsTrue(originalTiles[1].IsRenaming);

        // Membership and order changes retain existing tiles too.
        changes.Clear();
        var third = new BrightnessProfile { Name = "Third" };
        coordinator.Profiles.Insert(0, third);
        await coordinator.RenameProfileAsync(third, "Evening");
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Add }, changes);
        Assert.AreSame(originalTiles[1], viewModel.ProfileTiles[1]);
        changes.Clear();
        coordinator.Profiles.Move(1, 0);
        await coordinator.RenameProfileAsync(third, "Night");
        CollectionAssert.AreEqual(new[] { NotifyCollectionChangedAction.Move }, changes);
        Assert.AreSame(originalTiles[1], viewModel.ProfileTiles[0]);
    });

    [TestMethod]
    public void RenameProfile_SavesTrimmedNameAndRejectsDuplicatesAndBlanks() => RunWithCoordinator(async (coordinator, store) =>
    {
        var first = new BrightnessProfile { Name = "First" };
        var second = new BrightnessProfile { Name = "Second" };
        coordinator.Profiles.Add(first);
        coordinator.Profiles.Add(second);

        await coordinator.RenameProfileAsync(first, "  Evening  ");
        Assert.AreEqual("Evening", first.Name);
        var saved = await store.LoadAsync();
        CollectionAssert.AreEqual(new[] { "Evening", "Second" }, saved.Profiles.Select(p => p.Name).ToArray());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.RenameProfileAsync(first, "second"));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => coordinator.RenameProfileAsync(first, "   "));
        Assert.AreEqual("Evening", first.Name);

        await coordinator.RenameProfileAsync(first, "EVENING");
        Assert.AreEqual("EVENING", first.Name);
    });

    [TestMethod]
    public void DeleteProfile_RemovesAndPersistsIt() => RunWithCoordinator(async (coordinator, store) =>
    {
        var first = new BrightnessProfile { Name = "First" };
        var second = new BrightnessProfile { Name = "Second" };
        var third = new BrightnessProfile { Name = "Third" };
        coordinator.Profiles.Add(first);
        coordinator.Profiles.Add(second);
        coordinator.Profiles.Add(third);
        Assert.IsFalse(coordinator.CanCreateProfile);

        await coordinator.DeleteProfileAsync(second);

        CollectionAssert.AreEqual(new[] { "First", "Third" }, coordinator.Profiles.Select(p => p.Name).ToArray());
        Assert.IsTrue(coordinator.CanCreateProfile);
        var saved = await store.LoadAsync();
        CollectionAssert.AreEqual(new[] { first.Id, third.Id }, saved.Profiles.Select(p => p.Id).ToArray());
    });

    [TestMethod]
    public void ProfileTile_RenamesInPlaceAndKeepsEditOpenOnError() => RunWithCoordinator(async (coordinator, _) =>
    {
        coordinator.Profiles.Add(new BrightnessProfile { Name = "First" });
        coordinator.Profiles.Add(new BrightnessProfile { Name = "Second" });
        coordinator.Profiles.Add(new BrightnessProfile { Name = "Third" });
        var viewModel = new FlyoutViewModel(coordinator);
        CollectionAssert.AreEqual(
            new[] { "First", "Second", "Third" },
            viewModel.ProfileTiles.Select(tile => tile.Name).ToArray());

        var tile = viewModel.ProfileTiles[0];
        viewModel.StartProfileRename(tile);
        Assert.IsTrue(tile.IsRenaming);
        Assert.IsTrue(viewModel.IsRenaming);
        Assert.AreEqual("First", tile.EditName);

        tile.EditName = "Second";
        await viewModel.SaveProfileNameAsync(tile);
        Assert.IsTrue(tile.IsRenaming);
        Assert.IsTrue(viewModel.HasProfileRenameMessage);

        tile.EditName = "  ";
        Assert.IsFalse(tile.CanSaveName);

        tile.EditName = "Work";
        await viewModel.SaveProfileNameAsync(tile);
        Assert.IsFalse(tile.IsRenaming);
        Assert.IsFalse(viewModel.HasProfileRenameMessage);
        Assert.AreEqual("Work", viewModel.ProfileTiles[0].Name);
        Assert.AreSame(tile, viewModel.ProfileTiles[0]);

        viewModel.StartProfileRename(viewModel.ProfileTiles[1]);
        viewModel.StartProfileRename(viewModel.ProfileTiles[2]);
        Assert.IsFalse(viewModel.ProfileTiles[1].IsRenaming, "Only one name is edited at a time.");
        viewModel.CancelRenames();
        Assert.IsFalse(viewModel.IsRenaming);
        Assert.AreEqual("Third", viewModel.ProfileTiles[2].Name);

        await viewModel.DeleteProfileAsync(viewModel.ProfileTiles[1]);
        CollectionAssert.AreEqual(
            new[] { "Work", "Third" },
            viewModel.ProfileTiles.Select(item => item.Name).ToArray());
    });

    [TestMethod]
    public void Dispose_WaitsForAProfileBeingApplied() => RunWithCoordinator(async (coordinator, _) =>
    {
        var writeStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var monitorService = new MonitorService();
        var row = CreateRow("monitor-1", monitorService, async value =>
        {
            writeStarted.TrySetResult();
            await releaseWrite.Task;
            return value;
        });
        coordinator.Monitors.Add(row);
        var profile = CreateProfile(("monitor-1", 30));
        coordinator.Profiles.Add(profile);

        var apply = coordinator.ApplyProfileAsync(profile);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var dispose = coordinator.DisposeAsync().AsTask();
        await Task.Delay(50);
        Assert.IsFalse(dispose.IsCompleted, "Disposal didn't wait for the profile being applied.");

        releaseWrite.SetResult();
        Assert.IsTrue(await apply);
        await dispose.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.IsFalse(await coordinator.ApplyProfileAsync(profile));
    });

    [TestMethod]
    public void ApplyProfile_IsNotActiveWhenTheUserCommitsWhileItsWriteRuns() => RunWithCoordinator(async (coordinator, _) =>
    {
        var profileWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProfileWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var monitorService = new MonitorService();
        var row = CreateRow("monitor-1", monitorService, async value =>
        {
            if (value == 30)
            {
                profileWriteStarted.TrySetResult();
                await releaseProfileWrite.Task;
            }

            return value;
        });
        coordinator.Monitors.Add(row);
        var profile = CreateProfile(("monitor-1", 30));
        coordinator.Profiles.Add(profile);

        var apply = coordinator.ApplyProfileAsync(profile);
        await profileWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The tile still shows 50, so committing it changes no value but replaces the profile's.
        var commit = row.CommitAsync();
        releaseProfileWrite.SetResult();

        Assert.IsFalse(await apply);
        await commit;
        Assert.IsNull(coordinator.ActiveProfileId);
    });

    [TestMethod]
    public void ApplyProfile_IsNotActiveWhenATileCommitReplacesItsQueuedWrite() => RunWithCoordinator(async (coordinator, _) =>
    {
        var tileWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseTileWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var writes = new List<int>();
        await using var monitorService = new MonitorService();
        var row = CreateRow("monitor-1", monitorService, async value =>
        {
            lock (writes)
            {
                writes.Add(value);
            }

            if (value == 50)
            {
                tileWriteStarted.TrySetResult();
                await releaseTileWrite.Task;
            }

            return value;
        });
        coordinator.Monitors.Add(row);
        var profile = CreateProfile(("monitor-1", 30));
        coordinator.Profiles.Add(profile);

        var firstCommit = row.CommitAsync();
        await tileWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var apply = coordinator.ApplyProfileAsync(profile);
        await Task.Delay(50);
        var secondCommit = row.CommitAsync();
        releaseTileWrite.SetResult();

        Assert.IsFalse(await apply);
        await Task.WhenAll(firstCommit, secondCommit);
        Assert.IsNull(coordinator.ActiveProfileId);
        CollectionAssert.DoesNotContain(writes, 30);
    });

    [TestMethod]
    public void ReconnectedDisplay_GetsAProfileThatWasStillBeingApplied() => RunWithCoordinator(async (coordinator, _) =>
    {
        var profileWriteStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseProfileWrite = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reconnectedWrites = new List<int>();
        await using var monitorService = new MonitorService();
        var connected = CreateRow("monitor-1", monitorService, async value =>
        {
            profileWriteStarted.TrySetResult();
            await releaseProfileWrite.Task;
            return value;
        });
        var reconnected = CreateRow("monitor-2", monitorService, value =>
        {
            lock (reconnectedWrites)
            {
                reconnectedWrites.Add(value);
            }

            return Task.FromResult(value);
        });
        coordinator.Monitors.Add(connected);
        var profile = CreateProfile(("monitor-1", 30), ("monitor-2", 70));
        coordinator.Profiles.Add(profile);

        var apply = coordinator.ApplyProfileAsync(profile);
        await profileWriteStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // The second display reconnects while no profile is active yet.
        coordinator.Monitors.Add(reconnected);
        var reconnect = coordinator.ApplyActiveProfileToRowsAsync([reconnected], CancellationToken.None);
        releaseProfileWrite.SetResult();

        Assert.IsTrue(await apply);
        await reconnect;
        CollectionAssert.AreEqual(new[] { 70 }, reconnectedWrites);
        Assert.AreEqual(profile.Id, coordinator.ActiveProfileId);
    });

    private static MonitorRowViewModel CreateRow(string id, MonitorService monitorService, Func<int, Task<int>> write) => new(
        new MonitorSnapshot(
            id, id, id, null, @"\\.\DISPLAY1", new DesktopBounds(0, 0, 1920, 1080),
            0, 100, 50, BrightnessBackendKind.DdcHighLevel, MonitorSupportStatus.Ready, null),
        monitorService,
        Dispatcher.CurrentDispatcher,
        (_, value, _, _) => write(value));

    private static BrightnessProfile CreateProfile(params (string Id, int Brightness)[] settings) => new()
    {
        Name = "Work",
        MonitorSettings = settings
            .Select(setting => new ProfileMonitorSetting
            {
                Identity = new MonitorIdentity { Id = setting.Id },
                Brightness = setting.Brightness
            })
            .ToList()
    };

    private static void RunWithCoordinator(Func<MonitorCoordinator, AppSettingsStore, Task> body)
    {
        var directory = Path.Combine(Path.GetTempPath(), "MonitorCenterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    var store = new AppSettingsStore(Path.Combine(directory, "settings.json"));
                    await using var monitorService = new MonitorService();
                    await using var coordinator = new MonitorCoordinator(monitorService, dispatcher, store);
                    await body(coordinator, store);
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

        try
        {
            Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Dispatcher test timed out.");
            if (failure is not null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
