using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class MonitorLabelerTests
{
    [TestMethod]
    public void OrderAndLabel_UsesDesktopOrderAndDisambiguatesDuplicates()
    {
        var monitors = new[]
        {
            Create("right", "LS27AG32x", 1920, "3990"),
            Create("left", "LS27AG32x", -1920, "0679"),
            Create("center", "27GN880", 0, "D001")
        };

        var result = MonitorLabeler.OrderAndLabel(monitors);

        CollectionAssert.AreEqual(
            new[] { "LS27AG32x (1)", "27GN880", "LS27AG32x (2)" },
            result.Select(monitor => monitor.DisplayName).ToArray());
        CollectionAssert.AreEqual(
            new[] { "left", "center", "right" },
            result.Select(monitor => monitor.Id).ToArray());
    }

    private static MonitorSnapshot Create(string id, string name, int left, string serial)
    {
        return new MonitorSnapshot(
            id,
            name,
            name,
            serial,
            id,
            new DesktopBounds(left, 0, left + 1920, 1080),
            0,
            100,
            50,
            BrightnessBackendKind.DdcHighLevel,
            MonitorSupportStatus.Ready,
            null);
    }
}
