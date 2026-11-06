using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.Basic;
using Lyntai.Providers.ClaudeCli;

namespace Lyntai.Providers.Acp;

/// <summary>The extensions the shipped agents state, one reader each. <see cref="Default"/> is what a door reads when
/// its registration names no set; a registration that names one reads exactly that, so a door for one agent can
/// leave out another agent's keys, and a host's own reader is added beside these.</summary>
public static class AcpExtensionReaders
{
    /// <summary><c>claude-agent-acp</c>: a rejected <c>_claude/rateLimit</c> frame on a <c>usage_update</c>, and the
    /// Claude category in an error's <c>data.errorKind</c>.</summary>
    public static IAcpExtensionReader Claude { get; } = new ClaudeExtensions();

    /// <summary><c>codex-acp</c>: an error's <c>data.codexErrorInfo</c>, a word or an object keyed by its variant.</summary>
    public static IAcpExtensionReader Codex { get; } = new CodexExtensions();

    /// <summary>The <c>sessionFailure</c> field of the AIR extension (<c>_meta.jetbrains.air</c>) the door declares at
    /// <c>initialize</c>: <c>codex-acp</c> ends a failed turn <c>end_turn</c> and says so only there.</summary>
    public static IAcpExtensionReader AirSessionFailure { get; } = new AirSessionFailureExtensions();

    /// <summary>All three, which is what a door reads when its registration names no set.</summary>
    public static IReadOnlyList<IAcpExtensionReader> Default { get; } = [Claude, Codex, AirSessionFailure];

    private sealed class ClaudeExtensions : IAcpExtensionReader
    {
        // claude-agent-acp forwards the CLI's rate_limit_info here unchanged; an "allowed" frame says nothing, so it clears nothing
        // every frame is an AgentAllowanceReport; a rejected one also holds the failure, and any other holds none and clears none
        public AcpUpdateReading? ReadUpdate(JsonElement update, TimeProvider time)
        {
            if (WireJson.String(update, "sessionUpdate") != "usage_update" ||
                WireJson.Object(update, "_meta") is not { } meta || WireJson.Object(meta, "_claude/rateLimit") is not { } info)
                return null;
            var report = StreamJsonFailure.ReadReport(info);
            return StreamJsonFailure.ReadRejectionInfo(info) is { } rejected
                ? new AcpUpdateReading([report], StreamJsonFailure.FromRejection(rejected)! with { By = "acp.usage_update._claude/rateLimit.rejected" })
                : new AcpUpdateReading([report]) { KeepsFailure = true };
        }

        public AgentFailure? ReadError(JsonElement error, TimeProvider time)
        {
            var errorKind = WireJson.Object(error, "data") is { } data ? WireJson.String(data, "errorKind") : null;
            return StreamJsonFailure.FromCategory(errorKind) is { } category ? category with { By = $"acp.data.errorKind={errorKind}" } : null;
        }
    }

    private sealed class CodexExtensions : IAcpExtensionReader
    {
        public AgentFailure? ReadError(JsonElement error, TimeProvider time) =>
            WireJson.Object(error, "data") is { } data && data.TryGetProperty("codexErrorInfo", out var info) ? FromInfo(info) : null;

        // a word, or an object keyed by the variant that holds its details (an HTTP status among them); a word
        // that says nothing ("other") reads as nothing, so the sentence may
        private static AgentFailure? FromInfo(JsonElement info)
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

    private sealed class AirSessionFailureExtensions : IAcpExtensionReader
    {
        // A warning is a notice such as a reconnect, and it replaces an earlier error the agent recovered from;
        // only the prompt's own answer ends a turn, since an earlier update's error may have been recovered from.
        public AcpUpdateReading? ReadUpdate(JsonElement update, TimeProvider time) =>
            WireJson.String(update, "sessionUpdate") == "session_info_update" && Read(update) is { } typed
                ? new AcpUpdateReading(Failure: typed.EndsTurn ? FromTyped(typed, time) : null)
                : null;

        public AcpStatedFailure? ReadTurnEnd(JsonElement answer, TimeProvider time) =>
            Read(answer) is { EndsTurn: true } typed
                ? new AcpStatedFailure($"acp.sessionFailure.category={typed.Category}", typed.Title, FromTyped(typed, time))
                : null;

        private static (string Category, bool EndsTurn, string? Title)? Read(JsonElement holder)
        {
            if (WireJson.Object(holder, "_meta") is not { } meta ||
                WireJson.Object(meta, "jetbrains") is not { } jetbrains ||
                WireJson.Object(jetbrains, "air") is not { } air ||
                WireJson.Object(air, AcpFrames.AirSessionFailure) is not { } failure)
                return null;

            return (WireJson.String(failure, "category") ?? "unknown", (WireJson.String(failure, "severity") ?? "error") == "error",
                WireJson.String(failure, "title"));
        }

        // The category word is the agent's; "limit" covers an allowance AND a spent context window, so it reads as a
        // limit only where the title's sentence says an allowance ran out.
        private static AgentFailure? FromTyped((string Category, bool EndsTurn, string? Title) typed, TimeProvider time)
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
    }
}
