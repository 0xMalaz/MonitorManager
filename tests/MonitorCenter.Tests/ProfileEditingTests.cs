using System.IO;
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
