using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class BrightnessMathTests
{
    [TestMethod]
    public void ToPercent_NormalizesNonStandardMonitorRange()
    {
        Assert.AreEqual(0, BrightnessMath.ToPercent(20, 20, 80));
        Assert.AreEqual(50, BrightnessMath.ToPercent(50, 20, 80));
        Assert.AreEqual(100, BrightnessMath.ToPercent(80, 20, 80));
    }

    [TestMethod]
    public void FromPercent_NormalizesAndClamps()
    {
        Assert.AreEqual(20u, BrightnessMath.FromPercent(-10, 20, 80));
        Assert.AreEqual(50u, BrightnessMath.FromPercent(50, 20, 80));
        Assert.AreEqual(80u, BrightnessMath.FromPercent(110, 20, 80));
    }

    [TestMethod]
    public void DegenerateRange_IsSafe()
    {
        Assert.AreEqual(0, BrightnessMath.ToPercent(40, 40, 40));
        Assert.AreEqual(40u, BrightnessMath.FromPercent(50, 40, 40));
    }
}
