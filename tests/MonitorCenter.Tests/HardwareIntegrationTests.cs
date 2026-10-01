using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
[TestCategory("Hardware")]
public sealed class HardwareIntegrationTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task DiscoversExpectedConnectedMonitorsAndSupportStates()
    {
        RequireEnvironmentVariable("MONITORCENTER_HARDWARE_TESTS");
        await using var service = new MonitorService();

        var monitors = await service.DiscoverAsync();
        WriteDiagnostics(monitors);

        Assert.AreEqual(3, monitors.Count, "Expected the three connected desktop monitors.");
        Assert.AreEqual(1, monitors.Count(monitor => monitor.FriendlyName == "27GN880"));
        Assert.AreEqual(2, monitors.Count(monitor => monitor.FriendlyName == "LS27AG32x"));

        var samsungs = monitors.Where(monitor => monitor.FriendlyName == "LS27AG32x").ToArray();
        var lg = monitors.Single(monitor => monitor.FriendlyName == "27GN880");

        Assert.IsTrue(lg.IsControllable, "The LG should expose DDC/CI brightness.");
        Assert.IsTrue(
            samsungs.Any(monitor => monitor.IsControllable),
            "At least one Samsung should expose brightness; either unit may be intermittently unavailable.");
        foreach (var unavailableSamsung in samsungs.Where(monitor => !monitor.IsControllable))
        {
            StringAssert.Contains(unavailableSamsung.ErrorMessage, "DDC/CI");
        }
    }

    [TestMethod]
    public async Task ReadyMonitors_AcceptSmallChangeAndRestoreOriginalBrightness()
    {
        RequireEnvironmentVariable("MONITORCENTER_HARDWARE_WRITE_TESTS");
        await using var service = new MonitorService();
        var monitors = await service.DiscoverAsync();
        var ready = monitors.Where(monitor => monitor.IsControllable).ToArray();

        Assert.IsTrue(ready.Length >= 2, "Expected at least the LG and one Samsung to be controllable.");

        foreach (var monitor in ready)
        {
            var original = monitor.CurrentPercent;
            var target = original <= 95 ? original + 5 : original - 5;
            TestContext.WriteLine($"Testing {monitor.DisplayName}: {original}% -> {target}% -> {original}%");

            try
            {
                var changed = await service.SetBrightnessAsync(monitor.Id, target);
                Assert.IsTrue(
                    Math.Abs(changed.ActualPercent - target) <= 1,
                    $"{monitor.DisplayName} read back {changed.ActualPercent}% after requesting {target}%.");
            }
            finally
            {
                BrightnessResult? restored = null;
                Exception? restoreFailure = null;

                for (var attempt = 0; attempt < 3 && restored is null; attempt++)
                {
                    try
                    {
                        var candidate = await service.SetBrightnessAsync(monitor.Id, original);
                        if (Math.Abs(candidate.ActualPercent - original) <= 1)
                        {
                            restored = candidate;
                        }
                    }
                    catch (Exception exception)
                    {
                        restoreFailure = exception;
                    }

                    if (restored is null)
                    {
                        await Task.Delay(150);
                    }
                }

                Assert.IsNotNull(
                    restored,
                    $"{monitor.DisplayName} was not restored to {original}%: {restoreFailure?.Message}");
            }
        }
    }

    private void WriteDiagnostics(IEnumerable<MonitorSnapshot> monitors)
    {
        foreach (var monitor in monitors)
        {
            TestContext.WriteLine(
                $"{monitor.DisplayName} | serial={monitor.Serial} | display={monitor.DisplayDevice} | " +
                $"backend={monitor.Backend} | status={monitor.Status} | brightness={monitor.CurrentPercent}%");
        }
    }

    private static void RequireEnvironmentVariable(string variable)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(variable), "1", StringComparison.Ordinal))
        {
            Assert.Inconclusive($"Set {variable}=1 to run this hardware integration test.");
        }
    }
}
