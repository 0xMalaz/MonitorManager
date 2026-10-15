using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
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

    [TestMethod]
    public void AutomationPeer_ExposesASliderThatScreenReadersCanSet()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var box = new BrightnessBox { Value = 40 };
                var commits = 0;
                box.ValueCommitRequested += (_, _) => commits++;
                var peer = UIElementAutomationPeer.CreatePeerForElement(box);

                Assert.AreEqual(AutomationControlType.Slider, peer.GetAutomationControlType());
                var range = (IRangeValueProvider)peer.GetPattern(PatternInterface.RangeValue);
                Assert.AreEqual(40d, range.Value);
                Assert.AreEqual(0d, range.Minimum);
                Assert.AreEqual(100d, range.Maximum);
                Assert.IsFalse(range.IsReadOnly);

                range.SetValue(65);
                Assert.AreEqual(65, box.Value);
                Assert.AreEqual(1, commits);

                box.IsInteractionEnabled = false;
                Assert.IsTrue(range.IsReadOnly);
                Assert.ThrowsExactly<ElementNotEnabledException>(() => range.SetValue(10));
                Assert.AreEqual(65, box.Value);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.IsTrue(thread.Join(TimeSpan.FromSeconds(20)), "Test timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
