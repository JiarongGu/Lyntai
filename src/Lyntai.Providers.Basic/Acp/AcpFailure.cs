using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.Basic;
using Lyntai.Providers.ClaudeCli;

namespace Lyntai.Providers.Acp;

/// <summary>A failure an agent states as a field on a session update or a prompt's answer, under the AIR
/// <c>sessionFailure</c> extension the client declares at <c>initialize</c>.</summary>
/// <param name="Category">Its category word: <c>limit</c>, <c>access</c>, <c>connection</c>, <c>service</c>,
/// <c>request</c> or <c>unknown</c>.</param>
/// <param name="Severity"><c>error</c> for a failure that ended the turn, <c>warning</c> for a notice such as a
/// reconnect that did not.</param>
/// <param name="Title">The agent's words for it.</param>
internal sealed record AcpTypedFailure(string Category, string Severity, string? Title)
{
    public bool EndsTurn => Severity == "error";

    /// <summary>The failure under <c>_meta.jetbrains.air.sessionFailure</c> of <paramref name="holder"/>, if any.</summary>
    public static AcpTypedFailure? Read(JsonElement holder)
    {
        if (WireJson.Object(holder, "_meta") is not { } meta ||
            WireJson.Object(meta, "jetbrains") is not { } jetbrains ||
            WireJson.Object(jetbrains, "air") is not { } air ||
            WireJson.Object(air, AcpFrames.AirSessionFailure) is not { } failure)
            return null;

        return new AcpTypedFailure(
            WireJson.String(failure, "category") ?? "unknown", WireJson.String(failure, "severity") ?? "error",
            WireJson.String(failure, "title"));
    }
}

/// <summary>How the ACP door reads its OWN failure fields — the JSON-RPC error's code and <c>data</c>, a typed
/// session failure, a rejected rate-limit frame the agent forwarded — before any sentence, and never the agent's
/// message text. The two agents measured put different fields there: <c>claude-agent-acp</c> an
/// <c>errorKind</c> (the Claude category, read by the same table the stream door uses), <c>codex-acp</c> a
/// <c>codexErrorInfo</c> beside the limit sentence in <c>data.message</c>, with <c>Internal error</c> alone as the
/// error's message.</summary>
internal static class AcpFailure
{
    /// <summary>The JSON-RPC code ACP gives an agent that wants a sign-in first.</summary>
    public const int AuthRequiredCode = -32000;

    /// <summary>A JSON-RPC error read as a failure.</summary>
    /// <param name="error">The response's <c>error</c> object.</param>
    /// <param name="rejected">A rejected limit frame seen earlier in the turn.</param>
    /// <param name="typed">A typed failure that ended the turn, seen earlier.</param>
    /// <param name="time">The clock a sentence's time is read against.</param>
    /// <param name="words">The door's words for it, for the terminal's diagnostic.</param>
    public static AgentFailure FromError(
        JsonElement error, RejectedLimit? rejected, AcpTypedFailure? typed, TimeProvider time, out string words)
    {
        var code = WireJson.Int32(error, "code");
        var message = WireJson.String(error, "message");
        var data = WireJson.Object(error, "data") ?? default;
        var detail = WireJson.String(data, "message");
        words = Words(message, detail);

        var errorKind = WireJson.String(data, "errorKind");
        var readings = new List<AgentFailure?>
        {
            Rejection(rejected),
            StreamJsonFailure.FromCategory(errorKind) is { } category ? category with { By = $"acp.data.errorKind={errorKind}" } : null,
            data.ValueKind == JsonValueKind.Object && data.TryGetProperty("codexErrorInfo", out var info) ? FromCodex(info) : null,
            code == AuthRequiredCode ? new AgentFailure(AgentFailureKind.Access, $"acp.error.code={code}") : null,
            typed is { EndsTurn: true } ? FromTyped(typed, time) : null,
        };
        return StreamJsonFailure.Merge(readings, ProviderVerdictClassifier.ReadAgentFailure(words, time), $"acp.error.code={code}");
    }

    /// <summary>A turn the agent ended <c>end_turn</c> that says, as a field, that it failed.</summary>
    public static AgentFailure FromTypedEnd(AcpTypedFailure typed, RejectedLimit? rejected, TimeProvider time) =>
        StreamJsonFailure.Merge([Rejection(rejected), FromTyped(typed, time)],
            ProviderVerdictClassifier.ReadAgentFailure(typed.Title, time), $"acp.sessionFailure.category={typed.Category}");

    public static ProviderVerdict VerdictOf(AgentFailure failure, string? words) =>
        ProviderVerdictClassifier.FromAgentFailure(failure, words);

    private static string Words(string? message, string? detail) =>
        (message, detail) switch
        {
            (null or "", _) => detail ?? "",
            (_, null or "") => message,
            _ => message.Contains(detail, StringComparison.Ordinal) ? message : $"{message}: {detail}",
        };

    private static AgentFailure? Rejection(RejectedLimit? rejected) =>
        StreamJsonFailure.FromRejection(rejected) is { } limit ? limit with { By = "acp.usage_update._claude/rateLimit.rejected" } : null;

    // The category word is the agent's; "limit" covers an allowance AND a spent context window, so it reads as a
    // limit only where the title's sentence says an allowance ran out.
    private static AgentFailure? FromTyped(AcpTypedFailure typed, TimeProvider time)
    {
        var by = $"acp.sessionFailure.category={typed.Category}";
        return typed.Category switch
        {
            "limit" => ProviderVerdictClassifier.ReadAgentFailure(typed.Title, time) is { Kind: AgentFailureKind.Limit } limit
                ? limit with { By = by }
                : new AgentFailure(AgentFailureKind.Fatal, by),
            "access" => new AgentFailure(AgentFailureKind.Access, by),
            "connection" or "service" => new AgentFailure(AgentFailureKind.Transient, by),
            "request" => new AgentFailure(AgentFailureKind.Fatal, by),
            _ => null,
        };
    }

    // Codex's error info: a word, or an object keyed by the variant that holds its details (an HTTP status among
    // them). A word that says nothing ("other") reads as nothing, so the sentence may.
    private static AgentFailure? FromCodex(JsonElement info)
    {
        if (info.ValueKind == JsonValueKind.String)
        {
            var word = info.GetString();
            var by = $"acp.data.codexErrorInfo={word}";
            return word switch
            {
                "usageLimitExceeded" => new AgentFailure(AgentFailureKind.Limit, by)
                    { Restriction = new(RestrictionCause.Quota, RestrictionSubject.Account, RestrictionRecovery.Unknown) },
                "rateLimitExceeded" => new AgentFailure(AgentFailureKind.Limit, by)
                    { Restriction = new(RestrictionCause.Rate, RestrictionSubject.Unknown, RestrictionRecovery.Unknown) },
                "unauthorized" => new AgentFailure(AgentFailureKind.Access, by),
                "serverOverloaded" => new AgentFailure(AgentFailureKind.Transient, by)
                    { Restriction = new(RestrictionCause.Capacity, RestrictionSubject.Endpoint, RestrictionRecovery.Unknown) },
                "internalServerError" => new AgentFailure(AgentFailureKind.Transient, by),
                "contextWindowExceeded" or "sessionBudgetExceeded" or "cyberPolicy" or "misalignmentPolicyViolation"
                    or "tooManyDenials" or "badRequest" or "threadRollbackFailed" or "sandboxError" => new AgentFailure(AgentFailureKind.Fatal, by),
                _ => null,
            };
        }

        if (info.ValueKind != JsonValueKind.Object) return null;
        foreach (var variant in info.EnumerateObject())
        {
            var by = $"acp.data.codexErrorInfo.{variant.Name}";
            switch (WireJson.Int32(variant.Value, "httpStatusCode"))
            {
                case 429: return new AgentFailure(AgentFailureKind.Limit, by)
                    { Restriction = new(RestrictionCause.Rate, RestrictionSubject.Unknown, RestrictionRecovery.Unknown) };
                case 401: return new AgentFailure(AgentFailureKind.Access, by);
                case >= 500 and < 600: return new AgentFailure(AgentFailureKind.Transient, by);
            }

            if (variant.Name is "httpConnectionFailed" or "responseStreamConnectionFailed"
                or "responseStreamDisconnected" or "responseTooManyFailedAttempts")
                return new AgentFailure(AgentFailureKind.Transient, by);
        }
        return null;
    }
}
