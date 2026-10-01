using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class FlyoutPlacementTests
{
    private static readonly PixelRect Screen = new(0, 0, 1920, 1080);

    [TestMethod]
    public void BottomTaskbar_PlacesFlyoutAboveWorkAreaEdge()
    {
        var result = FlyoutPlacement.Calculate(
            Screen,
            new PixelRect(0, 0, 1920, 1040),
            new PixelPoint(1800, 1060),
            new PixelSize(340, 280));

        Assert.AreEqual(new PixelPoint(1572, 752), result);
    }

    [TestMethod]
    public void TopTaskbar_PlacesFlyoutBelowWorkAreaEdge()
    {
        var result = FlyoutPlacement.Calculate(
            Screen,
            new PixelRect(0, 40, 1920, 1080),
            new PixelPoint(1000, 20),
            new PixelSize(340, 280));

        Assert.AreEqual(new PixelPoint(830, 48), result);
    }

    [TestMethod]
    public void LeftAndRightTaskbars_UseTheirAdjacentEdges()
    {
        var left = FlyoutPlacement.Calculate(
            Screen,
            new PixelRect(48, 0, 1920, 1080),
            new PixelPoint(20, 500),
            new PixelSize(340, 280));
        var right = FlyoutPlacement.Calculate(
            Screen,
            new PixelRect(0, 0, 1872, 1080),
            new PixelPoint(1900, 500),
            new PixelSize(340, 280));

        Assert.AreEqual(56, left.X);
        Assert.AreEqual(1524, right.X);
        Assert.AreEqual(360, left.Y);
        Assert.AreEqual(360, right.Y);
    }
}
