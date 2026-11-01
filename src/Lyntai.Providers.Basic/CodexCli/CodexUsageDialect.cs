using System.Buffers;
using System.Globalization;
using System.Text;
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
    private const int InitializeRequestId = 1;
    private const int UsageRequestId = 2;

    /// <summary>The versions whose signed-in response is recorded and read by <see cref="Read"/> in the tests.</summary>
    internal static readonly IReadOnlySet<string> Validated = new HashSet<string>(StringComparer.Ordinal) { "0.160.0" };

    public string ProviderId { get; } = providerId;

    public string HomeVariable => CodexAgentDoor.AccountHomeVariable;

    public ICliBackend Backend { get; } = new CodexCliBackend();

    public IReadOnlyList<string> VersionArgs { get; } = ["--version"];

    public IReadOnlySet<string> ValidatedVersions => Validated;

    public IReadOnlyList<string> ServerArgs { get; } = ["app-server"];

    public IReadOnlyList<UsageStep> StepsFor(string clientName, string clientVersion) =>
    [
        new([Initialize(clientName, clientVersion)], line => IsAnswerTo(line, InitializeRequestId)),
        new(["""{"method":"initialized"}""", $$"""{"id":{{UsageRequestId}},"method":"account/rateLimits/read","params":null}"""],
            line => IsAnswerTo(line, UsageRequestId)),
    ];

    // the host's client name and version are text, so a writer carries them and nothing splices them into the line
    private static string Initialize(string clientName, string clientVersion)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteNumber("id", InitializeRequestId);
            writer.WriteString("method", "initialize");
            writer.WriteStartObject("params");
            writer.WriteStartObject("clientInfo");
            writer.WriteString("name", clientName);
            writer.WriteString("version", clientVersion);
            writer.WriteEndObject();
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    public string? ParseVersion(string output) =>
        VersionPattern().Match(output) is { Success: true } m ? m.Groups[1].Value : null;

    public bool IsAnswer(string line) => IsAnswerTo(line, UsageRequestId);

    public string? FailureOf(string answer)
    {
        using var document = Answer(answer, InitializeRequestId);
        return document is not null && document.RootElement.TryGetProperty("error", out var error) ? ErrorReason(error) : null;
    }

    // `error` is an object whose message says whether the account is signed in; any other shape is not recognised
    private static string ErrorReason(JsonElement error) =>
        WireJson.String(error, "message") is { } message
        && message.Contains("authentication required", StringComparison.OrdinalIgnoreCase)
            ? AgentAllowanceUnknown.NotSignedIn
            : AgentAllowanceUnknown.Unrecognised;

    public AgentAllowanceReading Read(IReadOnlyList<string> lines, DateTimeOffset measuredAt)
    {
        var unknown = (string reason) => AgentAllowanceReading.Unknown(reason, measuredAt);
        try
        {
            JsonDocument? answer = null;
            foreach (var line in lines)
                if ((answer = Answer(line, UsageRequestId)) is not null) break;
            if (answer is null) return unknown(AgentAllowanceUnknown.Unrecognised);

            using (answer)
            {
                var root = answer.RootElement;
                if (WireJson.Object(root, "error") is { } error) return unknown(ErrorReason(error));

                if (WireJson.Object(root, "result") is not { } result
                    || WireJson.Object(result, "rateLimits") is not { } limits)
                    return unknown(AgentAllowanceUnknown.Unrecognised);

                var windows = new List<AgentAllowanceWindow>(2);
                if (!ReadWindows(limits, null, windows)) return unknown(AgentAllowanceUnknown.Unrecognised);

                // the other limits the server named, each as scoped windows. Without the own limit's id there is no
                // telling which entry is the one already read above, so none is taken.
                var limitId = WireJson.String(limits, "limitId");
                if (limitId is not null && result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind != JsonValueKind.Null)
                {
                    if (byId.ValueKind != JsonValueKind.Object) return unknown(AgentAllowanceUnknown.Unrecognised);
                    foreach (var other in byId.EnumerateObject())
                    {
                        if (other.Name == limitId || other.Value.ValueKind == JsonValueKind.Null) continue;
                        if (other.Value.ValueKind != JsonValueKind.Object || !ReadWindows(other.Value, other.Name, windows))
                            return unknown(AgentAllowanceUnknown.Unrecognised);
                    }
                }
                if (windows.Count == 0) return unknown(AgentAllowanceUnknown.Unrecognised);

                return new AgentAllowanceReading
                {
                    MeasuredAt = measuredAt, Windows = windows, LimitId = limitId, Plan = WireJson.String(limits, "planType"),
                };
            }
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex) || ex is ArgumentOutOfRangeException or OverflowException)
        {
            return unknown(AgentAllowanceUnknown.Unrecognised);
        }
    }

    // the primary and secondary windows of one rate-limit snapshot; false when a window states something unrecognisable
    private static bool ReadWindows(JsonElement snapshot, string? scope, List<AgentAllowanceWindow> into)
    {
        foreach (var property in new[] { "primary", "secondary" })
        {
            if (!snapshot.TryGetProperty(property, out var window) || window.ValueKind == JsonValueKind.Null) continue;
            if (window.ValueKind != JsonValueKind.Object || !WireJson.Percent(window, "usedPercent", out var used, out var step))
                return false;

            DateTimeOffset? resetsAt = null;
            if (window.TryGetProperty("resetsAt", out var reset) && reset.ValueKind != JsonValueKind.Null)
            {
                if (reset.ValueKind != JsonValueKind.Number || !reset.TryGetInt64(out var seconds)
                    || seconds is < 0 or > 253_402_300_799) return false;
                resetsAt = DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
            // a window that states neither a use nor a reset says nothing
            if (used is null && resetsAt is null) continue;

            if (WireJson.Int64(window, "windowDurationMins") is not { } minutes || minutes <= 0) return false;
            into.Add(new AgentAllowanceWindow(NameOf(minutes), used, TimeSpan.FromMinutes(minutes), resetsAt)
                { Scope = scope, UsedStep = step });
        }
        return true;
    }

    private static string NameOf(long minutes) => minutes switch
    {
        300 => AgentAllowanceWindow.FiveHour,
        10_080 => AgentAllowanceWindow.Weekly,
        _ => $"{minutes.ToString(CultureInfo.InvariantCulture)}-minutes",
    };

    private static bool IsAnswerTo(string line, int id)
    {
        using var answer = Answer(line, id);
        return answer is not null;
    }

    // the document of the JSON-RPC response that answers request <id>; the caller disposes it
    private static JsonDocument? Answer(string line, int id)
    {
        if (line.Length == 0 || line[0] != '{') return null;
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(line);
            // a request the server makes of the client can carry the same id; it carries a method, a response does not
            if (WireJson.Int32(document.RootElement, "id") == id && !document.RootElement.TryGetProperty("method", out _))
                return document;
        }
        catch (JsonException) { /* not a JSON line: the server printed something else */ }
        document?.Dispose();
        return null;
    }

    [GeneratedRegex(@"(\d+\.\d+\.\d+(?:-[0-9A-Za-z.]+)?)(?=\s|$)")]
    private static partial Regex VersionPattern();
}
