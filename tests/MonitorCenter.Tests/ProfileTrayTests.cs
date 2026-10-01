using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;
using MonitorCenter.Services;
using System.Windows.Threading;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class ProfileTrayTests
{
    [TestMethod]
    public async Task TrayProfiles_ReturnOnlyFirstThreeProfilesInSavedOrder()
    {
        await using var monitorService = new MonitorService();
        await using var coordinator = new MonitorCoordinator(monitorService, Dispatcher.CurrentDispatcher);
        foreach (var name in new[] { "First", "Second", "Third", "Fourth", "Fifth" })
        {
            coordinator.Profiles.Add(new BrightnessProfile { Name = name });
        }

        CollectionAssert.AreEqual(
            new[] { "First", "Second", "Third" },
            coordinator.TrayProfiles.Select(profile => profile.Name).ToArray());
    }

    [TestMethod]
    public async Task TrayProfiles_ReturnAllProfilesWhenFewerThanThreeExist()
    {
        await using var monitorService = new MonitorService();
        await using var coordinator = new MonitorCoordinator(monitorService, Dispatcher.CurrentDispatcher);
        coordinator.Profiles.Add(new BrightnessProfile { Name = "First" });
        coordinator.Profiles.Add(new BrightnessProfile { Name = "Second" });

        CollectionAssert.AreEqual(
            new[] { "First", "Second" },
            coordinator.TrayProfiles.Select(profile => profile.Name).ToArray());
        Assert.IsTrue(coordinator.CanCreateProfile);
    }

    [TestMethod]
    public async Task ProfileCapacity_StopsAtThree()
    {
        await using var monitorService = new MonitorService();
        await using var coordinator = new MonitorCoordinator(monitorService, Dispatcher.CurrentDispatcher);
        coordinator.Profiles.Add(new BrightnessProfile { Name = "First" });
        coordinator.Profiles.Add(new BrightnessProfile { Name = "Second" });
        coordinator.Profiles.Add(new BrightnessProfile { Name = "Third" });

        Assert.IsFalse(coordinator.CanCreateProfile);
        Assert.AreEqual(MonitorCoordinator.MaxProfileCount, coordinator.TrayProfiles.Count());
    }
}
