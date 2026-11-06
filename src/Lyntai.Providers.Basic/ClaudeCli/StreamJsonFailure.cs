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
    public static RejectedLimit? ReadRejection(JsonElement root) =>
        WireJson.Object(root, "rate_limit_info") is { } info ? ReadRejectionInfo(info) : null;

    /// <summary>The rejection a <c>rate_limit_info</c> object states, which the protocol door finds under
    /// <c>_meta["_claude/rateLimit"]</c> of a <c>usage_update</c>, unchanged.</summary>
    public static RejectedLimit? ReadRejectionInfo(JsonElement info)
    {
        if (WireJson.String(info, "status") != "rejected") return null;

        var type = WireJson.String(info, "rateLimitType");
        var seconds = WireJson.Int64(info, type == "overage" ? "overageResetsAt" : "resetsAt")
            ?? WireJson.Int64(info, "resetsAt");
        return new RejectedLimit(type, seconds is { } s ? DateTimeOffset.FromUnixTimeSeconds(s) : null,
            WireJson.String(info, "limitScope"));
    }

    /// <summary>The report a <c>rate_limit_info</c> object states, allowed or rejected; the window is read as
    /// <see cref="AgentAllowanceWindow"/> states one; its use is a fraction from 0 to 1 in the recorded frames (0.88 is 88%).</summary>
    public static AgentAllowanceReport ReadReport(JsonElement info)
    {
        var type = WireJson.String(info, "rateLimitType");
        var overageResets = WireJson.Int64(info, "overageResetsAt") is { } o ? DateTimeOffset.FromUnixTimeSeconds(o) : (DateTimeOffset?)null;
        var seconds = WireJson.Int64(info, type == "overage" ? "overageResetsAt" : "resetsAt");
        var resets = seconds is { } s ? DateTimeOffset.FromUnixTimeSeconds(s) : (DateTimeOffset?)null;
        // the window's own entry among the report's windows states its use, and its reset when the report's own states none
        var entry = type is { Length: > 0 } && WireJson.Object(info, "unifiedWindows") is { } windows ? WireJson.Object(windows, type) : null;
        if (resets is null && entry is { } own && WireJson.Int64(own, "resetsAt") is { } entryReset)
            resets = DateTimeOffset.FromUnixTimeSeconds(entryReset);
        var use = (entry is { } e ? WireJson.Fraction(e, "utilization") : null) ?? WireJson.Fraction(info, "utilization");
        AgentAllowanceWindow? window = type switch
        {
            null or "" => null,
            "five_hour" => new AgentAllowanceWindow(AgentAllowanceWindow.FiveHour, null, TimeSpan.FromHours(5), resets),
            "seven_day" => new AgentAllowanceWindow(AgentAllowanceWindow.Weekly, null, TimeSpan.FromDays(7), resets),
            _ when type.StartsWith("seven_day_", StringComparison.Ordinal) && type.Length > "seven_day_".Length =>
                new AgentAllowanceWindow(AgentAllowanceWindow.Weekly, null, TimeSpan.FromDays(7), resets) { Scope = type["seven_day_".Length..] },
            _ => new AgentAllowanceWindow(type, null, null, resets),
        };
        if (window is not null && use is { } read) window = window with { UsedPercent = read.Percent, UsedStep = read.Step };
        bool? overage = info.TryGetProperty("isUsingOverage", out var flag) && flag.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? flag.ValueKind == JsonValueKind.True
            : null;
        return new AgentAllowanceReport(WireJson.String(info, "status"), window, WireJson.String(info, "limitScope"), overage, overageResets)
        {
            OverageStatus = WireJson.String(info, "overageStatus"),
            OverageDisabledReason = WireJson.String(info, "overageDisabledReason"),
        };
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
        return Merge([FromRejection(rejected), FromStatus(status), FromCategory(category)], sentence, "claude.result");
    }

    /// <summary>The most severe reading among a door's fields (limit, then access, then transient, then fatal), else
    /// the sentence, else fatal; a sentence that agrees with the field's kind fills what the field left open and
    /// never overrides it. Shared with the protocol door, which reads the same categories off an error's
    /// <c>data</c>.</summary>
    /// <param name="readings">What each of the door's fields read as; null for a field that said nothing.</param>
    /// <param name="sentence">The door's words read as a failure, or null.</param>
    /// <param name="unreadBy">The <see cref="AgentFailure.By"/> of a failure no field and no sentence read.</param>
    internal static AgentFailure Merge(IEnumerable<AgentFailure?> readings, AgentFailure? sentence, string unreadBy)
    {
        var fields = readings
            .OfType<AgentFailure>()
            .OrderBy(f => Rank(f.Kind))
            .ThenBy(f => f.Restriction is null ? 1 : 0)
            .ToList();
        if (fields.Count == 0)
            return sentence is { } s ? s with { By = $"{unreadBy}:{s.By}" } : new AgentFailure(AgentFailureKind.Fatal, unreadBy);

        var field = fields[0];
        if (field.Kind == AgentFailureKind.Access)
        {
            // the field's reading and the words' stay apart: the first field that stated one, and what a sentence that
            // reads as access implies
            var stated = fields.Where(f => f.Kind == AgentFailureKind.Access).Select(f => f.AccessReadings?.ByField).FirstOrDefault(r => r is not null);
            var implied = sentence is { Kind: AgentFailureKind.Access } ? sentence.AccessReadings?.ByWords : null;
            field = field with { AccessReadings = new AgentAccessReadings(stated, implied) };
        }

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

    internal static AgentFailure? FromRejection(RejectedLimit? r) => r is null ? null : new AgentFailure(AgentFailureKind.Limit, "claude.rate_limit_event.rejected")
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
            401 => Access(by, AccessReason.CredentialRefused),
            403 => Access(by),
            529 => new AgentFailure(AgentFailureKind.Transient, by)
                { Restriction = new(RestrictionCause.Capacity, RestrictionSubject.Endpoint, RestrictionRecovery.Unknown) },
            408 or 500 or 502 or 503 or 504 => new AgentFailure(AgentFailureKind.Transient, by),
            >= 400 and < 500 => new AgentFailure(AgentFailureKind.Fatal, by),
            _ => null,
        };
    }

    internal static AgentFailure? FromCategory(string? category)
    {
        var by = $"claude.assistant.error={category}";
        return category switch
        {
            "rate_limit" => new AgentFailure(AgentFailureKind.Limit, by)
                { Restriction = new(RestrictionCause.Rate, RestrictionSubject.Unknown, RestrictionRecovery.Unknown) },
            "billing_error" => new AgentFailure(AgentFailureKind.Limit, by)
                { Restriction = new(RestrictionCause.Credit, RestrictionSubject.Account, RestrictionRecovery.OwnerActionFor(OwnerActionReason.Refill)) },
            "authentication_failed" or "oauth_org_not_allowed" or "account_on_hold" or "verification_required"
                or "cloud_credential_error" => Access(by),
            "overloaded" => new AgentFailure(AgentFailureKind.Transient, by)
                { Restriction = new(RestrictionCause.Capacity, RestrictionSubject.Endpoint, RestrictionRecovery.Unknown) },
            "server_error" => new AgentFailure(AgentFailureKind.Transient, by),
            "model_not_found" => ModelRefused(by),
            "invalid_request" or "max_output_tokens" => new AgentFailure(AgentFailureKind.Fatal, by),
            _ => null,   // "unknown" and anything new say nothing, so the sentence may
        };
    }

    /// <summary>A model the agent will not run: fatal, and held against the one model on the one account — the pool
    /// skips that seat and nothing wider — until a person acts on it, since waiting does not help.</summary>
    internal static AgentFailure ModelRefused(string by) => new(AgentFailureKind.Fatal, by)
    {
        Restriction = new AgentRestriction(RestrictionCause.Unknown, RestrictionSubject.Model, RestrictionRecovery.OwnerActionFor(OwnerActionReason.Configure)),
    };

    /// <summary>An access failure a field decided. <paramref name="stated"/> is what that field says of the action;
    /// no Claude category states one, since a failed authentication is a sign-in or a refusal alike.</summary>
    internal static AgentFailure Access(string by, AccessReason? stated = null) =>
        new(AgentFailureKind.Access, by) { AccessReadings = new AgentAccessReadings(stated, null) };

    private static AgentRestriction Open() => new(RestrictionCause.Unknown, RestrictionSubject.Unknown, RestrictionRecovery.Unknown);
}
