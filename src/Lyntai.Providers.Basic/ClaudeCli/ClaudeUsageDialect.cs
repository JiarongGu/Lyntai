using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Lyntai.Agents;
using Lyntai.Inference.Cli;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>The <c>claude</c> CLI's answer to its <c>get_usage</c> control request, which it answers without a
/// prompt and without a model request. The request is declared experimental by the vendor, so the reader takes
/// only the fields a validated version was seen to send and reads anything else as unknown.</summary>
internal sealed partial class ClaudeUsageDialect(string providerId) : IUsageDialect
{
    internal const string UsageRequestId = "usage";

    /// <summary>The versions whose signed-in response is recorded and read by <see cref="Read"/> in the tests.</summary>
    internal static readonly IReadOnlySet<string> Validated = new HashSet<string>(StringComparer.Ordinal) { "2.1.296" };

    public string ProviderId { get; } = providerId;

    public string HomeVariable => ClaudeAgentDoor.AccountHomeVariable;

    public ICliBackend Backend { get; } = new ClaudeCliBackend();

    public IReadOnlyList<string> VersionArgs { get; } = ["--version"];

    public IReadOnlySet<string> ValidatedVersions => Validated;

    public IReadOnlyList<string> ServerArgs { get; } =
        ["-p", "--input-format", "stream-json", "--output-format", "stream-json", "--verbose", "--strict-mcp-config"];

    public IReadOnlyList<string> Requests { get; } =
    [
        """{"type":"control_request","request_id":"init","request":{"subtype":"initialize"}}""",
        $$$"""{"type":"control_request","request_id":"{{{UsageRequestId}}}","request":{"subtype":"get_usage","skip_behaviors":true}}""",
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
            JsonDocument? response = null;
            foreach (var line in lines)
                if ((response = Answer(line)) is not null) break;
            if (response is null) return unknown(AgentAllowanceUnknown.Unrecognised);

            using (response)
            {
                var envelope = response.RootElement.GetProperty("response");
                if (WireJson.String(envelope, "subtype") != "success") return unknown(AgentAllowanceUnknown.Unrecognised);
                if (WireJson.Object(envelope, "response") is not { } body) return unknown(AgentAllowanceUnknown.Unrecognised);

                if (!body.TryGetProperty("rate_limits_available", out var available)
                    || available.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return unknown(AgentAllowanceUnknown.Unrecognised);
                if (available.ValueKind == JsonValueKind.False) return unknown(AgentAllowanceUnknown.NotReported);

                if (WireJson.Object(body, "rate_limits") is not { } limits) return unknown(AgentAllowanceUnknown.Unrecognised);
                var windows = new List<AgentAllowanceWindow>(2);
                foreach (var (property, name, length) in Windows)
                {
                    if (!limits.TryGetProperty(property, out var window) || window.ValueKind == JsonValueKind.Null) continue;
                    if (window.ValueKind != JsonValueKind.Object) return unknown(AgentAllowanceUnknown.Unrecognised);
                    if (!Whole(window, "utilization", out var used) || used is null or < 0 or > 100) return unknown(AgentAllowanceUnknown.Unrecognised);
                    if (!Moment(window, "resets_at", out var resetsAt)) return unknown(AgentAllowanceUnknown.Unrecognised);
                    windows.Add(new AgentAllowanceWindow(name, used, length, resetsAt));
                }
                if (windows.Count == 0) return unknown(AgentAllowanceUnknown.Unrecognised);

                return new AgentAllowanceReading { MeasuredAt = measuredAt, Windows = windows, Plan = WireJson.String(body, "subscription_type") };
            }
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return unknown(AgentAllowanceUnknown.Unrecognised);
        }
    }

    private static readonly (string Property, string Name, TimeSpan Length)[] Windows =
    [
        ("five_hour", AgentAllowanceWindow.FiveHour, TimeSpan.FromHours(5)),
        ("seven_day", AgentAllowanceWindow.Weekly, TimeSpan.FromDays(7)),
    ];

    // the document of the control_response that answers the usage request; the caller disposes it
    private static JsonDocument? Answer(string line)
    {
        if (line.Length == 0 || line[0] != '{') return null;
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (WireJson.String(root, "type") == "control_response"
                && WireJson.Object(root, "response") is { } response
                && WireJson.String(response, "request_id") == UsageRequestId)
                return document;
        }
        catch (JsonException) { /* not a JSON line: the server printed something else */ }
        document?.Dispose();
        return null;
    }

    private static bool Whole(JsonElement parent, string name, out int? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null) return true;
        if (el.ValueKind != JsonValueKind.Number || !el.TryGetInt32(out var number)) return false;
        value = number;
        return true;
    }

    private static bool Moment(JsonElement parent, string name, out DateTimeOffset? value)
    {
        value = null;
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null) return true;
        if (el.ValueKind != JsonValueKind.String
            || !DateTimeOffset.TryParse(el.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var moment))
            return false;
        value = moment;
        return true;
    }

    [GeneratedRegex(@"^\s*(\d+\.\d+\.\d+)(?=\s|$)")]
    private static partial Regex VersionPattern();
}
