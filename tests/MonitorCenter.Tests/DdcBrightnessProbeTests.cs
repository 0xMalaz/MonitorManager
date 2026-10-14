using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class DdcBrightnessProbeTests
{
    private static readonly DdcBrightnessReading HighLevel = new(BrightnessBackendKind.DdcHighLevel, 0, 40, 100);
    private static readonly DdcBrightnessReading Vcp = new(BrightnessBackendKind.DdcVcp, 0, 30, 100);

    [TestMethod]
    public void Probe_WhenReadsFailThenSucceed_RetriesAfterAPause()
    {
        var highLevel = new ScriptedReader(null, null, HighLevel);
        var vcp = new ScriptedReader();
        var sleeps = new List<TimeSpan>();

        var reading = DdcBrightnessProbe.Probe(highLevel.Read, vcp.Read, BrightnessBackendKind.None, sleeps.Add);

        Assert.AreEqual(HighLevel, reading);
        Assert.AreEqual(3, highLevel.Calls);
        CollectionAssert.AreEqual(DdcBrightnessProbe.RetryDelays.Take(2).ToArray(), sleeps);
    }

    [TestMethod]
    public void Probe_WhenHighLevelFails_FallsBackToVcpInTheSameAttempt()
    {
        var highLevel = new ScriptedReader();
        var vcp = new ScriptedReader(Vcp);
        var sleeps = new List<TimeSpan>();

        var reading = DdcBrightnessProbe.Probe(highLevel.Read, vcp.Read, BrightnessBackendKind.None, sleeps.Add);

        Assert.AreEqual(Vcp, reading);
        Assert.AreEqual(0, sleeps.Count);
    }

    [TestMethod]
    public void Probe_WhenEveryAttemptFails_GivesUpAfterAllRetries()
    {
        var highLevel = new ScriptedReader();
        var vcp = new ScriptedReader();
        var sleeps = new List<TimeSpan>();

        var reading = DdcBrightnessProbe.Probe(highLevel.Read, vcp.Read, BrightnessBackendKind.None, sleeps.Add);

        Assert.IsNull(reading);
        Assert.AreEqual(DdcBrightnessProbe.RetryDelays.Length + 1, highLevel.Calls);
        Assert.AreEqual(DdcBrightnessProbe.RetryDelays.Length + 1, vcp.Calls);
        CollectionAssert.AreEqual(DdcBrightnessProbe.RetryDelays, sleeps);
    }

    [TestMethod]
    public void Probe_TreatsAnInvalidRangeAsAFailedRead()
    {
        var highLevel = new ScriptedReader(
            HighLevel with { Minimum = 50, Maximum = 50 },
            HighLevel with { Maximum = 0 },
            HighLevel);
        var vcp = new ScriptedReader(Vcp with { Maximum = 0 }, Vcp with { Maximum = 0 });

        var reading = DdcBrightnessProbe.Probe(highLevel.Read, vcp.Read, BrightnessBackendKind.None, _ => { });

        Assert.AreEqual(HighLevel, reading);
        Assert.AreEqual(3, highLevel.Calls);
    }

    [TestMethod]
    public void Probe_AsksAKnownVcpDisplayWithVcpFirst()
    {
        var highLevel = new ScriptedReader(HighLevel);
        var vcp = new ScriptedReader(Vcp);

        var reading = DdcBrightnessProbe.Probe(highLevel.Read, vcp.Read, BrightnessBackendKind.DdcVcp, _ => { });

        Assert.AreEqual(Vcp, reading);
        Assert.AreEqual(0, highLevel.Calls);
    }

    /// <summary>Returns the scripted readings in order, then fails every later read.</summary>
    private sealed class ScriptedReader(params DdcBrightnessReading?[] readings)
    {
        public int Calls { get; private set; }

        public DdcBrightnessReading? Read()
        {
            var index = Calls++;
            return index < readings.Length ? readings[index] : null;
        }
    }
}
