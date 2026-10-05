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
    public async Task DiscoversConnectedMonitorsWithConsistentSupportStates()
    {
        RequireEnvironmentVariable("MONITORCENTER_HARDWARE_TESTS");
        await using var service = new MonitorService();

        var monitors = await service.DiscoverAsync();
        WriteDiagnostics(monitors);

        Assert.IsTrue(monitors.Count > 0, "Expected at least one connected display.");
        Assert.AreEqual(
            monitors.Count,
            monitors.Select(monitor => monitor.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            "Display IDs should be unique.");

        foreach (var monitor in monitors)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(monitor.DisplayName), $"{monitor.Id} has no display name.");
            if (monitor.IsControllable)
            {
                Assert.IsNull(monitor.ErrorMessage, $"{monitor.DisplayName} is controllable but reports an error.");
                Assert.IsTrue(
                    monitor.CurrentPercent is >= 0 and <= 100,
                    $"{monitor.DisplayName} reported {monitor.CurrentPercent}%.");
            }
            else
            {
                Assert.AreEqual(BrightnessBackendKind.None, monitor.Backend);
                StringAssert.Contains(monitor.ErrorMessage, "DDC/CI");
            }
        }

        // Rediscovering without reprobing known displays must describe the same displays.
        var rediscovered = await service.DiscoverAsync(reprobeKnownDisplays: false);
        CollectionAssert.AreEqual(
            monitors.Select(Describe).ToArray(),
            rediscovered.Select(Describe).ToArray());

        static string Describe(MonitorSnapshot monitor) =>
            $"{monitor.Id}|{monitor.FriendlyName}|{monitor.DisplayName}|{monitor.Serial}|{monitor.Backend}|{monitor.Status}";
    }

    [TestMethod]
    public async Task ReadyMonitors_AcceptSmallChangeAndRestoreOriginalBrightness()
    {
        RequireEnvironmentVariable("MONITORCENTER_HARDWARE_WRITE_TESTS");
        await using var service = new MonitorService();
        var monitors = await service.DiscoverAsync();
        var ready = monitors.Where(monitor => monitor.IsControllable).ToArray();

        if (ready.Length == 0)
        {
            Assert.Inconclusive("No connected display exposes controllable brightness.");
        }

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
