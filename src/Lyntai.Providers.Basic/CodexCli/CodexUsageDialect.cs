using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lyntai.Agents;
using Lyntai.Inference.Cli;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.CodexCli;

/// <summary>The <c>codex</c> CLI's app server (JSON-RPC over stdio) and its <c>account/rateLimits/read</c>, which
/// spends no model usage. The server ends when its stdin closes, before it answers a request still in flight, so
/// it is asked through an exchange that keeps stdin open. The reader takes only the fields a validated version was
/// seen to send; the account id the answer carries is neither read nor kept.</summary>
internal sealed partial class CodexUsageDialect(string providerId) : IUsageDialect
{
    private const int UsageRequestId = 2;

    /// <summary>The versions whose signed-in response is recorded and read by <see cref="Read"/> in the tests.</summary>
    internal static readonly IReadOnlySet<string> Validated = new HashSet<string>(StringComparer.Ordinal) { "0.160.0" };

    public string ProviderId { get; } = providerId;

    public string HomeVariable => CodexAgentDoor.AccountHomeVariable;

    public ICliBackend Backend { get; } = new CodexCliBackend();

    public IReadOnlyList<string> VersionArgs { get; } = ["--version"];

    public IReadOnlySet<string> ValidatedVersions => Validated;

    public IReadOnlyList<string> ServerArgs { get; } = ["app-server"];

    public IReadOnlyList<string> Requests { get; } =
    [
        """{"id":1,"method":"initialize","params":{"clientInfo":{"name":"lyntai","version":"1"}}}""",
        """{"method":"initialized"}""",
        $$"""{"id":{{UsageRequestId}},"method":"account/rateLimits/read","params":null}""",
    ];

    public string? ParseVersion(string output) =>
        VersionPattern().Match(output) is { Success: true } m ? m.Groups[1].Value : null;

    public bool IsAnswer(string line)
    {
        using var answer = Answer(line);
        return answer is not null;
    }

    public AgentAllowanceReading Read(IReadOnlyList<string> lines, DateTimeOffset measuredAt)
    {
        var unknown = (string reason) => AgentAllowanceReading.Unknown(reason, measuredAt);
        try
        {
            JsonDocument? answer = null;
            foreach (var line in lines)
                if ((answer = Answer(line)) is not null) break;
            if (answer is null) return unknown(AgentAllowanceUnknown.Unrecognised);

            using (answer)
            {
                var root = answer.RootElement;
                if (WireJson.Object(root, "error") is { } error)
                    return unknown(WireJson.String(error, "message") is { } message
                        && message.Contains("authentication required", StringComparison.OrdinalIgnoreCase)
                        ? AgentAllowanceUnknown.NotSignedIn
                        : AgentAllowanceUnknown.Unrecognised);

                if (WireJson.Object(root, "result") is not { } result
                    || WireJson.Object(result, "rateLimits") is not { } limits)
                    return unknown(AgentAllowanceUnknown.Unrecognised);

                var windows = new List<AgentAllowanceWindow>(2);
                foreach (var property in new[] { "primary", "secondary" })
                {
                    if (!limits.TryGetProperty(property, out var window) || window.ValueKind == JsonValueKind.Null) continue;
                    if (window.ValueKind != JsonValueKind.Object) return unknown(AgentAllowanceUnknown.Unrecognised);

                    var used = WireJson.Int32(window, "usedPercent");
                    var minutes = WireJson.Int64(window, "windowDurationMins");
                    if (used is null or < 0 or > 100 || minutes is null or <= 0) return unknown(AgentAllowanceUnknown.Unrecognised);

                    DateTimeOffset? resetsAt = null;
                    if (window.TryGetProperty("resetsAt", out var reset) && reset.ValueKind != JsonValueKind.Null)
                    {
                        if (reset.ValueKind != JsonValueKind.Number || !reset.TryGetInt64(out var seconds)
                            || seconds is < 0 or > 253_402_300_799) return unknown(AgentAllowanceUnknown.Unrecognised);
                        resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
                    }
                    windows.Add(new AgentAllowanceWindow(NameOf(minutes.Value), used, TimeSpan.FromMinutes(minutes.Value), resetsAt));
                }
                if (windows.Count == 0) return unknown(AgentAllowanceUnknown.Unrecognised);

                return new AgentAllowanceReading { MeasuredAt = measuredAt, Windows = windows, Plan = WireJson.String(limits, "planType") };
            }
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex) || ex is ArgumentOutOfRangeException or OverflowException)
        {
            return unknown(AgentAllowanceUnknown.Unrecognised);
        }
    }

    private static string NameOf(long minutes) => minutes switch
    {
        300 => AgentAllowanceWindow.FiveHour,
        10_080 => AgentAllowanceWindow.Weekly,
        _ => $"{minutes.ToString(CultureInfo.InvariantCulture)}-minutes",
    };

    // the document of the JSON-RPC response that answers the usage request; the caller disposes it
    private static JsonDocument? Answer(string line)
    {
        if (line.Length == 0 || line[0] != '{') return null;
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(line);
            // a request the server makes of the client can carry the same id; it carries a method, a response does not
            if (WireJson.Int32(document.RootElement, "id") == UsageRequestId && !document.RootElement.TryGetProperty("method", out _))
                return document;
        }
        catch (JsonException) { /* not a JSON line: the server printed something else */ }
        document?.Dispose();
        return null;
    }

    [GeneratedRegex(@"(\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?)(?=\s|$)")]
    private static partial Regex VersionPattern();
}
