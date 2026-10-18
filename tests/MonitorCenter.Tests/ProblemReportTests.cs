using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MonitorCenter.Models;
using MonitorCenter.Services;

namespace MonitorCenter.Tests;

[TestClass]
public sealed class ProblemReportTests
{
    [TestMethod]
    public void Request_CarriesVersionSummaryDescriptionAndReport()
    {
        var json = ProblemReportSender.BuildRequest("1.11.0", "\n  KTC monitor is missing  \nIt never shows up.", "== System");

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        Assert.AreEqual("1.11.0", root.GetProperty("version").GetString());
        Assert.AreEqual("KTC monitor is missing", root.GetProperty("summary").GetString());
        Assert.AreEqual(
            "Description:\nKTC monitor is missing  \nIt never shows up.\n\n== System",
            root.GetProperty("report").GetString());
    }

    [TestMethod]
    public void Request_WithoutDescription_SaysSoAndHasAnEmptySummary()
    {
        using var document = JsonDocument.Parse(ProblemReportSender.BuildRequest("1.11.0", "  ", "report"));

        Assert.AreEqual(string.Empty, document.RootElement.GetProperty("summary").GetString());
        StringAssert.StartsWith(document.RootElement.GetProperty("report").GetString(), "Description:\n(none)\n\n");
    }

    [TestMethod]
    public void Request_LongSummary_IsShortened()
    {
        using var document = JsonDocument.Parse(ProblemReportSender.BuildRequest("1.0.0", new string('a', 200), "report"));

        Assert.AreEqual(81, document.RootElement.GetProperty("summary").GetString()!.Length);
    }

    [TestMethod]
    public void Request_OversizedReport_KeepsAsMuchAsFitsBelowTheServiceLimit()
    {
        // Quotes and newlines are escaped, so the JSON is much longer than the text itself.
        var report = string.Concat(Enumerable.Repeat("\"display\"\n", 20_000));

        var json = ProblemReportSender.BuildRequest("1.0.0", "too long", report);

        Assert.IsTrue(json.Length <= ProblemReportSender.MaxRequestLength, $"{json.Length} characters");
        Assert.IsTrue(json.Length > ProblemReportSender.MaxRequestLength - 100, $"Only {json.Length} characters were kept.");
        using var document = JsonDocument.Parse(json);
        var sent = document.RootElement.GetProperty("report").GetString()!;
        StringAssert.StartsWith(sent, "Description:\ntoo long\n\n\"display\"");
        StringAssert.EndsWith(sent, ProblemReportSender.TruncatedMarker);
    }

    [TestMethod]
    public void Request_Truncation_NeverSplitsASurrogatePair()
    {
        var report = string.Concat(Enumerable.Repeat("a😀", 40_000));

        using var document = JsonDocument.Parse(ProblemReportSender.BuildRequest("1.0.0", new string('b', 79) + "😀", report));

        var sent = document.RootElement.GetProperty("report").GetString()!;
        var summary = document.RootElement.GetProperty("summary").GetString()!;
        Assert.IsFalse(sent.Contains('�') || summary.Contains('�'), "A character was cut in half.");
        Assert.AreEqual(new string('b', 79) + "…", summary);
    }

    [TestMethod]
    public void Prefix_StepsBackOverAHalfCharacter()
    {
        Assert.AreEqual("a", ProblemReportSender.Prefix("a😀b", 2));
        Assert.AreEqual("a😀", ProblemReportSender.Prefix("a😀b", 3));
        Assert.AreEqual("a😀b", ProblemReportSender.Prefix("a😀b", 10));
        Assert.AreEqual(string.Empty, ProblemReportSender.Prefix("a😀b", 0));
    }

    [TestMethod]
    public void Collect_WhenCancelled_StopsBeforeTouchingAnyDisplay()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsExactly<OperationCanceledException>(
            () => DiagnosticsReport.Collect("1.0.0", [], cancellation.Token));
    }

    [TestMethod]
    public void Request_KeepsTurkishLettersUnescaped()
    {
        var json = ProblemReportSender.BuildRequest("1.0.0", "Ekran görünmüyor", "ı ğ ş");

        StringAssert.Contains(json, "Ekran görünmüyor");
        StringAssert.Contains(json, "ı ğ ş");
    }

    [TestMethod]
    public async Task Send_PostsTheRequestAsJson()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK);
        using var sender = new ProblemReportSender(handler, "https://reports.example/");

        await sender.SendAsync("1.0.0", "broken", "report", CancellationToken.None);

        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual(new Uri("https://reports.example/"), handler.Uri);
        Assert.AreEqual("application/json", handler.MediaType);
        Assert.AreEqual(ProblemReportSender.BuildRequest("1.0.0", "broken", "report"), handler.Body);
    }

    [TestMethod]
    public async Task Send_WhenTheServiceRefuses_Throws()
    {
        using var sender = new ProblemReportSender(new RecordingHandler(HttpStatusCode.BadGateway), "https://reports.example/");

        await Assert.ThrowsExactlyAsync<HttpRequestException>(
            () => sender.SendAsync("1.0.0", string.Empty, "report", CancellationToken.None));
    }

    [TestMethod]
    public void SerialNumbers_AreReplacedByAStableShortHash()
    {
        var masked = DiagnosticsReport.MaskSerial("ABC123456");

        Assert.AreEqual(masked, DiagnosticsReport.MaskSerial(" ABC123456 "));
        Assert.AreNotEqual(masked, DiagnosticsReport.MaskSerial("ABC123457"));
        StringAssert.Matches(masked, new System.Text.RegularExpressions.Regex("^#[0-9A-F]{8}$"));
        Assert.AreEqual("-", DiagnosticsReport.MaskSerial(null));
        Assert.AreEqual("-", DiagnosticsReport.MaskSerial(" "));
    }

    [TestMethod]
    public void AppView_ListsEachDisplayWithoutItsSerial()
    {
        var report = new StringBuilder();
        DiagnosticsReport.WriteAppView(report,
        [
            new MonitorSnapshot(
                "DISPLAY\\ACI27A1\\1", "ASUS VG27A", "ASUS VG27A", "SERIAL-1", @"\\.\DISPLAY2",
                new DesktopBounds(0, 0, 2560, 1440), 0, 100, 70,
                BrightnessBackendKind.DdcVcp, MonitorSupportStatus.Ready, null),
            new MonitorSnapshot(
                "DISPLAY\\KTC2701\\2", "KTC", "KTC", null, @"\\.\DISPLAY3",
                new DesktopBounds(2560, 0, 4480, 1080), 0, 100, 0,
                BrightnessBackendKind.None, MonitorSupportStatus.Unavailable, "Brightness unavailable")
        ]);

        var text = report.ToString();
        StringAssert.Contains(text, "\"ASUS VG27A\" device=\\\\.\\DISPLAY2");
        StringAssert.Contains(text, "status=Ready control=DdcVcp raw=0/70/100");
        StringAssert.Contains(text, $"serial={DiagnosticsReport.MaskSerial("SERIAL-1")}");
        StringAssert.Contains(text, "status=Unavailable control=None");
        StringAssert.Contains(text, "error=\"Brightness unavailable\"");
        Assert.IsFalse(text.Contains("SERIAL-1"), "The serial number must not appear in the report.");
    }

    [TestMethod]
    public void SharedIds_AreTheOnesMoreThanOneDesktopAreaWouldGet()
    {
        var shared = DiagnosticsReport.FindSharedIds(["DISPLAY\\BOE0B49\\1", "display\\boe0b49\\1", "DISPLAY\\ACI27A1\\2"]);

        Assert.AreEqual(1, shared.Count);
        Assert.IsTrue(shared.Contains("DISPLAY\\BOE0B49\\1"));
        Assert.IsFalse(shared.Contains("DISPLAY\\ACI27A1\\2"));
    }

    private sealed class RecordingHandler(HttpStatusCode status) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public Uri? Uri { get; private set; }
        public string? MediaType { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri;
            MediaType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status);
        }
    }
}
