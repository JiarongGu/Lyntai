using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>A <c>rate_limit_event</c> the CLI sent with <c>status: "rejected"</c>.</summary>
/// <param name="Type">The window it names (<c>five_hour</c>, <c>seven_day</c>, <c>overage</c>…).</param>
/// <param name="ResetsAt">When that window renews, from the frame's Unix seconds.</param>
/// <param name="Scope">The spend limit that blocked, when it is not the member's own.</param>
internal sealed record RejectedLimit(string? Type, DateTimeOffset? ResetsAt, string? Scope);

/// <summary>How the claude door reads its OWN failure fields — a rejected <c>rate_limit_event</c>, the result's
/// <c>api_error_status</c>, the failed assistant message's <c>error</c> category — before any sentence, so a result
/// labelled <c>success</c> with <c>is_error: true</c> never reads as a clean end. The sentences stay in
/// <see cref="ProviderVerdictClassifier"/>; here a sentence only fills a detail a field left open.</summary>
internal static class StreamJsonFailure
{
    /// <summary>The frame's rejection, or null for any other status (<c>allowed</c> on every ordinary frame).</summary>
    public static RejectedLimit? ReadRejection(JsonElement root)
    {
        if (WireJson.Object(root, "rate_limit_info") is not { } info || WireJson.String(info, "status") != "rejected")
            return null;

        var type = WireJson.String(info, "rateLimitType");
        var seconds = WireJson.Int64(info, type == "overage" ? "overageResetsAt" : "resetsAt")
            ?? WireJson.Int64(info, "resetsAt");
        return new RejectedLimit(type, seconds is { } s ? DateTimeOffset.FromUnixTimeSeconds(s) : null,
            WireJson.String(info, "limitScope"));
    }

    /// <summary>The failure of an errored <c>result</c>: the most severe reading among the door's fields
    /// (limit, then access, then transient, then fatal), else the sentence, else fatal.</summary>
    /// <param name="status">The result's <c>api_error_status</c>.</param>
    /// <param name="category">The failed assistant message's <c>error</c>.</param>
    /// <param name="rejected">A rejected frame seen earlier in the turn.</param>
    /// <param name="text">The result's words — the door's error text when it is an error.</param>
    /// <param name="time">The clock a sentence's time is read against.</param>
    public static AgentFailure Read(int? status, string? category, RejectedLimit? rejected, string? text, TimeProvider time)
    {
        var sentence = ProviderVerdictClassifier.ReadAgentFailure(text, time);

        var fields = new[] { FromRejection(rejected), FromStatus(status), FromCategory(category) }
            .OfType<AgentFailure>()
            .OrderBy(f => Rank(f.Kind))
            .ToList();
        if (fields.Count == 0)
            return sentence is { } s ? s with { By = $"claude.result:{s.By}" } : new AgentFailure(AgentFailureKind.Fatal, "claude.result");

        var field = fields[0];
        if (sentence is null || sentence.Kind != field.Kind || sentence.Restriction is not { } said) return field;

        // the sentence agrees with the field's kind, so it may fill what the field left open — never override it
        var held = field.Restriction;
        return field with
        {
            Restriction = new AgentRestriction(
                held is { Cause: not RestrictionCause.Unknown } ? held.Cause : said.Cause,
                held is { Subject: not RestrictionSubject.Unknown } ? held.Subject : said.Subject,
                held is { Recovery.Kind: not RecoveryKind.Unknown } ? held.Recovery : said.Recovery),
        };
    }

    private static int Rank(AgentFailureKind kind) => kind switch
    {
        AgentFailureKind.Limit => 0,
        AgentFailureKind.Access => 1,
        AgentFailureKind.Transient => 2,
        _ => 3,
    };

    private static AgentFailure? FromRejection(RejectedLimit? r) => r is null ? null : new AgentFailure(AgentFailureKind.Limit, "claude.rate_limit_event.rejected")
    {
        Restriction = new AgentRestriction(
            r.Type == "overage" ? RestrictionCause.Credit : RestrictionCause.Quota,
            r.Scope == "group_pool" ? RestrictionSubject.Pool : RestrictionSubject.Account,
            r.ResetsAt is { } at ? RestrictionRecovery.ResetAt(at) : RestrictionRecovery.Unknown),
    };

    private static AgentFailure? FromStatus(int? status)
    {
        var by = $"claude.api_error_status={status}";
        return status switch
        {
            null => null,
            429 => new AgentFailure(AgentFailureKind.Limit, by) { Restriction = Open() },
            401 or 403 => new AgentFailure(AgentFailureKind.Access, by),
            529 => new AgentFailure(AgentFailureKind.Transient, by)
                { Restriction = new(RestrictionCause.Capacity, RestrictionSubject.Endpoint, RestrictionRecovery.Unknown) },
            408 or 500 or 502 or 503 or 504 => new AgentFailure(AgentFailureKind.Transient, by),
            >= 400 and < 500 => new AgentFailure(AgentFailureKind.Fatal, by),
            _ => null,
        };
    }

    private static AgentFailure? FromCategory(string? category)
    {
        var by = $"claude.assistant.error={category}";
        return category switch
        {
            "rate_limit" => new AgentFailure(AgentFailureKind.Limit, by)
                { Restriction = new(RestrictionCause.Rate, RestrictionSubject.Unknown, RestrictionRecovery.Unknown) },
            "billing_error" => new AgentFailure(AgentFailureKind.Limit, by)
                { Restriction = new(RestrictionCause.Credit, RestrictionSubject.Account, RestrictionRecovery.OwnerAction) },
            "authentication_failed" or "oauth_org_not_allowed" or "account_on_hold" or "verification_required"
                or "cloud_credential_error" => new AgentFailure(AgentFailureKind.Access, by),
            "overloaded" => new AgentFailure(AgentFailureKind.Transient, by)
                { Restriction = new(RestrictionCause.Capacity, RestrictionSubject.Endpoint, RestrictionRecovery.Unknown) },
            "server_error" => new AgentFailure(AgentFailureKind.Transient, by),
            "invalid_request" or "model_not_found" or "max_output_tokens" => new AgentFailure(AgentFailureKind.Fatal, by),
            _ => null,   // "unknown" and anything new say nothing, so the sentence may
        };
    }

    private static AgentRestriction Open() => new(RestrictionCause.Unknown, RestrictionSubject.Unknown, RestrictionRecovery.Unknown);
}
