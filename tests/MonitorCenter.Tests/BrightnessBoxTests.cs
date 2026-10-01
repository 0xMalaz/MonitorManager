using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.UI;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class BrightnessBoxTests
{
    [TestMethod]
    public void ValueFromY_MapsBottomMiddleAndTopToBrightness()
    {
        Assert.AreEqual(0, BrightnessBox.ValueFromY(100, 100));
        Assert.AreEqual(50, BrightnessBox.ValueFromY(50, 100));
        Assert.AreEqual(100, BrightnessBox.ValueFromY(0, 100));
    }

    [TestMethod]
    public void ValueFromY_ClampsPointerOutsideControl()
    {
        Assert.AreEqual(100, BrightnessBox.ValueFromY(-20, 100));
        Assert.AreEqual(0, BrightnessBox.ValueFromY(120, 100));
    }

    [TestMethod]
    public void ValueFromY_HandlesInvalidHeightSafely()
    {
        Assert.AreEqual(0, BrightnessBox.ValueFromY(20, 0));
        Assert.AreEqual(0, BrightnessBox.ValueFromY(20, double.NaN));
    }
}
