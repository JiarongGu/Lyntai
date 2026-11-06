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
    internal const string InitializeRequestId = "init";
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

    // the control request names no client, so the host's name and version have nowhere to go
    private static readonly IReadOnlyList<UsageStep> Exchange =
    [
        new([$$$"""{"type":"control_request","request_id":"{{{InitializeRequestId}}}","request":{"subtype":"initialize"}}"""],
            line => IsAnswerTo(line, InitializeRequestId)),
        new([$$$"""{"type":"control_request","request_id":"{{{UsageRequestId}}}","request":{"subtype":"get_usage","skip_behaviors":true}}"""],
            line => IsAnswerTo(line, UsageRequestId)),
    ];

    public IReadOnlyList<UsageStep> StepsFor(string clientName, string clientVersion) => Exchange;

    public string? ParseVersion(string output) =>
        VersionPattern().Match(output) is { Success: true } m ? m.Groups[1].Value : null;

    public bool IsAnswer(string line) => IsAnswerTo(line, UsageRequestId);

    // a refusal is a response whose subtype is not "success"; no capture shows one that says signed out, so none is read so
    public string? FailureOf(string answer)
    {
        using var response = Answer(answer, InitializeRequestId);
        return response is not null && WireJson.String(response.RootElement.GetProperty("response"), "subtype") != "success"
            ? AgentAllowanceUnknown.Unrecognised
            : null;
    }

    public AgentAllowanceReading Read(IReadOnlyList<string> lines, DateTimeOffset measuredAt)
    {
        var unknown = (string reason) => AgentAllowanceReading.Unknown(reason, measuredAt);
        try
        {
            JsonDocument? response = null;
            foreach (var line in lines)
                if ((response = Answer(line, UsageRequestId)) is not null) break;
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
                // the account's own windows, then each scoped week in the order the vendor lists them
                var candidates = Windows.Select(w => (w.Property, w.Name, w.Length, Scope: (string?)null)).ToList();
                foreach (var property in limits.EnumerateObject())
                    if (property.Name.Length > ScopedWeekPrefix.Length && property.Name.StartsWith(ScopedWeekPrefix, StringComparison.Ordinal))
                        candidates.Add((property.Name, AgentAllowanceWindow.Weekly, TimeSpan.FromDays(7), property.Name[ScopedWeekPrefix.Length..]));

                var windows = new List<AgentAllowanceWindow>(candidates.Count);
                foreach (var (property, name, length, scope) in candidates)
                {
                    if (!limits.TryGetProperty(property, out var window) || window.ValueKind == JsonValueKind.Null) continue;
                    if (window.ValueKind != JsonValueKind.Object
                        || !WireJson.Percent(window, "utilization", out var used, out var step)
                        || !Moment(window, "resets_at", out var resetsAt))
                        return unknown(AgentAllowanceUnknown.Unrecognised);
                    // a window that states neither a use nor a reset says nothing
                    if (used is null && resetsAt is null) continue;
                    windows.Add(new AgentAllowanceWindow(name, used, length, resetsAt) { Scope = scope, UsedStep = step });
                }
                if (windows.Count == 0) return unknown(AgentAllowanceUnknown.Unrecognised);

                if (!Rows(limits, out var rows)) return unknown(AgentAllowanceUnknown.Unrecognised);

                return new AgentAllowanceReading
                {
                    MeasuredAt = measuredAt, Windows = windows, Limits = rows, Plan = WireJson.String(body, "subscription_type"),
                };
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

    private static bool IsAnswerTo(string line, string requestId)
    {
        using var answer = Answer(line, requestId);
        return answer is not null;
    }

    // the document of the control_response that answers request <requestId>; the caller disposes it
    private static JsonDocument? Answer(string line, string requestId)
    {
        if (line.Length == 0 || line[0] != '{') return null;
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (WireJson.String(root, "type") == "control_response"
                && WireJson.Object(root, "response") is { } response
                && WireJson.String(response, "request_id") == requestId)
                return document;
        }
        catch (JsonException) { /* not a JSON line: the server printed something else */ }
        document?.Dispose();
        return null;
    }

    private const string ScopedWeekPrefix = "seven_day_";

    // rate_limits.limits[]: each row by kind and scope; the row's severity is the server's judgement and is not read
    private static bool Rows(JsonElement limits, out List<AgentAllowanceLimit> rows)
    {
        rows = [];
        if (!limits.TryGetProperty("limits", out var array) || array.ValueKind == JsonValueKind.Null) return true;
        if (array.ValueKind != JsonValueKind.Array) return false;
        foreach (var row in array.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || WireJson.String(row, "kind") is not { Length: > 0 } kind) return false;
            string? scope = null, scopeName = null, surface = null;
            if (row.TryGetProperty("scope", out var scoped) && scoped.ValueKind != JsonValueKind.Null
                && !Scope(scoped, out scope, out scopeName, out surface))
                return false;
            if (!WireJson.Percent(row, "percent", out var used, out var step) || !Moment(row, "resets_at", out var resetsAt)) return false;
            rows.Add(new AgentAllowanceLimit(kind, scope, used, step, resetsAt) { ScopeName = scopeName, Surface = surface });
        }
        return true;
    }

    // a scope is a string, or an object {"model": {"id", "display_name"} | null, "surface": string | null}; an object naming
    // neither is the account's own, and a present model or surface of another shape is unrecognised, not dropped
    private static bool Scope(JsonElement scoped, out string? scope, out string? name, out string? surface)
    {
        scope = name = surface = null;
        if (scoped.ValueKind == JsonValueKind.String)
        {
            scope = scoped.GetString();
            return true;
        }
        if (scoped.ValueKind != JsonValueKind.Object) return false;
        if (scoped.TryGetProperty("model", out var model) && model.ValueKind != JsonValueKind.Null)
        {
            if (model.ValueKind != JsonValueKind.Object || WireJson.String(model, "id") is not { Length: > 0 } id) return false;
            scope = id;
            name = WireJson.String(model, "display_name");
        }
        if (scoped.TryGetProperty("surface", out var surfaced) && surfaced.ValueKind != JsonValueKind.Null)
        {
            if (surfaced.ValueKind != JsonValueKind.String) return false;
            surface = surfaced.GetString();
        }
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
