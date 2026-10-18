using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Interop;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

/// <summary>
/// Which display a desktop area belongs to, modelled on a laptop with two graphics chips where every output of the
/// NVIDIA chip lists the built-in screen first, marked not active (report issue #4).
/// </summary>
[TestClass]
public sealed class DisplayIdentityTests
{
    private const string BuiltIn = @"\\?\DISPLAY#BOE0D55#5&65d2a47&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string Ktc = @"\\?\DISPLAY#SKG2505#5&65d2a47&0&UID4353#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const string Asus = @"\\?\DISPLAY#AUS25A8#5&65d2a47&0&UID4355#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}";
    private const uint Attached = 0x2;
    private const uint ActiveAndAttached = 0x3;

    [TestMethod]
    public void ShownDisplay_IsTheActiveOne_NotTheFirstListed()
    {
        var ktcOutput = SelectShown(Display(BuiltIn, Attached), Display(Ktc, ActiveAndAttached));
        var asusOutput = SelectShown(Display(BuiltIn, Attached), Display(Asus, ActiveAndAttached));

        Assert.AreEqual(Ktc, ktcOutput);
        Assert.AreEqual(Asus, asusOutput);
        Assert.AreNotEqual(
            WmiMonitorReader.NormalizeInstanceName(ktcOutput!),
            WmiMonitorReader.NormalizeInstanceName(asusOutput!));
    }

    [TestMethod]
    public void ShownDisplay_WithoutAnActiveOne_IsTheFirstListed()
    {
        Assert.AreEqual(BuiltIn, SelectShown(Display(BuiltIn, Attached), Display(Ktc, Attached)));
        Assert.AreEqual(Ktc, SelectShown(Display(Ktc, ActiveAndAttached)));
        Assert.IsNull(SelectShown());
    }

    [TestMethod]
    public void DesktopAreas_NeverShareAnId()
    {
        const string id = @"DISPLAY\BOE0D55\5&65D2A47&0&UID4352";

        Assert.AreEqual(id, MonitorService.UniqueBaseId(id, @"\\.\DISPLAY1", []));
        Assert.AreEqual($@"{id}@\\.\DISPLAY2", MonitorService.UniqueBaseId(id, @"\\.\DISPLAY2", [id]));
        Assert.AreEqual($@"{id}@\\.\DISPLAY2", MonitorService.UniqueBaseId(id, @"\\.\DISPLAY2", [$"{id}:1"]));
        Assert.AreEqual(id, MonitorService.UniqueBaseId(id, @"\\.\DISPLAY2", [$"{id}0"]));
    }

    private static string? SelectShown(params NativeMethods.DisplayDevice[] displays) =>
        MonitorService.SelectShownDisplay(displays)?.DeviceId;

    private static NativeMethods.DisplayDevice Display(string id, uint flags) =>
        new() { DeviceId = id, StateFlags = flags, DeviceString = "Generic PnP Monitor" };
}
