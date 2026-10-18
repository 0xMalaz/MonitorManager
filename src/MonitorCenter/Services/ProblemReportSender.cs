using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace MonitorCenter.Services;

/// <summary>
/// Sends a problem report to the MonitorCenter report service, which files it as an issue in a private GitHub
/// repository. The service holds the GitHub credentials; this app only knows its address.
/// </summary>
/// <remarks>The service lives in its own repository (monitorcenter-reports, a Cloudflare Worker).</remarks>
internal sealed class ProblemReportSender : IDisposable
{
    internal const string Endpoint = "https://monitorcenter-reports.mert-malaz.workers.dev";

    /// <summary>Points reports at another address instead, for testing without filing real issues.</summary>
    internal const string EndpointOverrideVariable = "MONITORCENTER_REPORT_ENDPOINT";

    /// <summary>
    /// The longest request the service accepts is 60,000 characters, because a GitHub issue holds 65,536; this
    /// leaves some room below it.
    /// </summary>
    internal const int MaxRequestLength = 58_000;

    internal const string TruncatedMarker = "\n[report truncated]";

    private const int MaxSummaryLength = 80;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Keeps Turkish letters as they are instead of \u escapes, which would make the request longer.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly HttpClient _client;
    private readonly Uri _endpoint;

    public ProblemReportSender(HttpMessageHandler? handler = null, string? endpoint = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler);
        _client.Timeout = TimeSpan.FromSeconds(30);
        var overrideEndpoint = Environment.GetEnvironmentVariable(EndpointOverrideVariable);
        _endpoint = new Uri(endpoint ?? (string.IsNullOrWhiteSpace(overrideEndpoint) ? Endpoint : overrideEndpoint));
    }

    /// <summary>Sends the report; throws when the service can't be reached or doesn't accept it.</summary>
    public async Task SendAsync(string version, string description, string report, CancellationToken cancellationToken)
    {
        using var content = new StringContent(BuildRequest(version, description, report), Encoding.UTF8, "application/json");
        using var response = await _client.PostAsync(_endpoint, content, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The report service answered {(int)response.StatusCode}.");
        }
    }

    /// <summary>
    /// The JSON the service expects: the version, a one-line summary for the issue title, and the description
    /// followed by the diagnostics, shortened when needed so the request stays under <see cref="MaxRequestLength"/>.
    /// </summary>
    internal static string BuildRequest(string version, string description, string report)
    {
        var text = ComposeText(description, report);
        var summary = Summarize(description);

        while (true)
        {
            var json = JsonSerializer.Serialize(new { version, summary, report = text }, JsonOptions);
            if (json.Length <= MaxRequestLength)
            {
                return json;
            }

            // Escaping makes the JSON longer than the text, so cut by the overshoot plus a margin and measure again.
            var keep = Math.Max(0, text.Length - (json.Length - MaxRequestLength) - TruncatedMarker.Length - 256);
            text = text[..keep] + TruncatedMarker;
        }
    }

    /// <summary>Saves the report where the user can find it, for when it can't be sent.</summary>
    public static string SaveToFile(string description, string report)
    {
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MonitorCenter",
            "Reports");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"MonitorCenter-report-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, ComposeText(description, report), Encoding.UTF8);
        return path;
    }

    private static string ComposeText(string description, string report) =>
        $"Description:\n{(string.IsNullOrWhiteSpace(description) ? "(none)" : description.Trim())}\n\n{report}";

    private static string Summarize(string description)
    {
        var firstLine = description
            .Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault() ?? string.Empty;
        return firstLine.Length <= MaxSummaryLength ? firstLine : firstLine[..MaxSummaryLength] + "…";
    }

    public void Dispose() => _client.Dispose();
}
