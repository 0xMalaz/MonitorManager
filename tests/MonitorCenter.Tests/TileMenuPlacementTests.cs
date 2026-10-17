using System.Windows;
using System.Windows.Controls.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.UI;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class TileMenuPlacementTests
{
    [TestMethod]
    public void Menu_OpensUnderTheButtonWithRightEdgesAligned()
    {
        var margin = TileMenuPlacement.ShadowMargin;
        var menu = new Size(140 + margin.Left + margin.Right, 41 + margin.Top + margin.Bottom);
        var button = new Size(22, 22);

        var placements = TileMenuPlacement.Calculate(menu, button, scale: 1);

        var below = placements[0].Point;
        Assert.AreEqual(button.Width, below.X + menu.Width - margin.Right, 0.001, "Right edges should line up.");
        Assert.AreEqual(button.Height + 3, below.Y + margin.Top, 0.001, "The menu should start just under the button.");
        Assert.AreEqual(PopupPrimaryAxis.Horizontal, placements[0].PrimaryAxis);

        var above = placements[1].Point;
        Assert.AreEqual(below.X, above.X, 0.001);
        Assert.AreEqual(-3, above.Y + menu.Height - margin.Bottom, 0.001, "Near the screen bottom it opens above instead.");
    }

    [TestMethod]
    public void Menu_ScalesItsOffsetsWithTheDisplay()
    {
        var margin = TileMenuPlacement.ShadowMargin;
        var menu = new Size((140 + margin.Left + margin.Right) * 1.5, (41 + margin.Top + margin.Bottom) * 1.5);
        var button = new Size(33, 33);

        var below = TileMenuPlacement.Calculate(menu, button, scale: 1.5)[0].Point;

        Assert.AreEqual(button.Width, below.X + menu.Width - margin.Right * 1.5, 0.001);
        Assert.AreEqual(button.Height + 3 * 1.5, below.Y + margin.Top * 1.5, 0.001);
    }
}
