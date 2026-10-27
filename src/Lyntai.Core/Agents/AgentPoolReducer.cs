using System.Collections.Immutable;

namespace Lyntai.Agents;

/// <summary>One held restriction. <see cref="Scope"/> is the canonical key of what it covers; <see cref="Id"/> adds
/// its cause, so independent causes on one scope are independent holds.</summary>
internal sealed record HoldRow(
    string Id, long Revision, string Scope, AgentRestriction Restriction, DateTimeOffset ObservedAt,
    DateTimeOffset? ReleasedAt, DateTimeOffset? LeaseUntil, string? LeaseToken);

internal readonly record struct PoolRules(TimeSpan AttemptLease, TimeSpan? UnknownProbeAfter);

/// <summary>Whether a seat is permitted: every covering hold must be released and free of an attempt. When not,
/// <see cref="ReadyAt"/> is the latest moment among the covering holds, or null when one of them waits on the host.</summary>
internal sealed record SeatAdmission(bool Permitted, IReadOnlyList<HoldRow> Probing, DateTimeOffset? ReadyAt);

/// <summary>The restriction reducer: pure functions from a set of holds, the clock and an event to the next set.
/// It owns no clock, no store and no lock; <see cref="AgentAccountPool"/> supplies those.
/// <para><b>The rules.</b> Holds are a set, and a seat is permitted only if EVERY hold covering it permits. A hold
/// is blocked until its release moment (a reset or retry-not-before time; an unknown recovery only when the host
/// sets <see cref="AgentPoolOptions.UnknownProbeAfter"/>; an owner action never) or until the host clears it by id
/// and revision. Released is not recovered: a released hold permits ONE attempt at a time, leased for
/// <see cref="AgentPoolOptions.AttemptLease"/>; that attempt's success removes the hold it was admitted under, and
/// its failure restates the hold at a higher revision. A newer failure is never undone by older evidence — a
/// success, a clear or a reading names the revision or the time it is about, and one that does not match the hold's
/// is ignored.</para></summary>
internal static class AgentPoolReducer
{
    // ── scopes ───────────────────────────────────────────────────────────────────────────────────

    private static string Esc(string part) => Uri.EscapeDataString(part);

    public static string AccountScope(string provider, string account) => $"account|{Esc(provider)}|{Esc(account)}";

    // a null model (the backend's default) and an empty one must not collide
    public static string SeatScope(string provider, string account, string? model) =>
        $"seat|{Esc(provider)}|{Esc(account)}|{(model is null ? "-" : "+" + Esc(model))}";

    public static string EndpointScope(string provider) => $"endpoint|{Esc(provider)}";

    public static string AllowanceScope(string allowance) => $"allowance|{Esc(allowance)}";

    /// <summary>Every scope that holds a seat: its account, its model on that account, its provider's endpoint and,
    /// where a door supplied one, the allowance its account draws on.</summary>
    public static IReadOnlyList<string> Covering(AgentAccount account, string? model, string? allowance)
    {
        var scopes = new List<string>(4)
        {
            AccountScope(account.Provider, account.Name),
            SeatScope(account.Provider, account.Name, model),
            EndpointScope(account.Provider),
        };
        if (allowance is not null) scopes.Add(AllowanceScope(allowance));
        return scopes;
    }

    public static string Label(string scope)
    {
        var parts = scope.Split('|').Select(Uri.UnescapeDataString).ToArray();
        return parts[0] switch
        {
            "account" => $"account {parts[1]}/{parts[2]}",
            "seat" => parts[3] == "-" ? $"account {parts[1]}/{parts[2]}, default model"
                : $"account {parts[1]}/{parts[2]}, model {parts[3][1..]}",
            "endpoint" => $"endpoint of {parts[1]}",
            _ => $"allowance {parts[1]}",
        };
    }

    // ── phases ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>When time or a clear releases the hold; null while only the host can.</summary>
    public static DateTimeOffset? ReleaseTime(HoldRow hold, PoolRules rules)
    {
        DateTimeOffset? natural = hold.Restriction.Recovery switch
        {
            { Kind: RecoveryKind.ResetAt or RecoveryKind.RetryNotBefore, At: { } at } => at,
            { Kind: RecoveryKind.OwnerAction } => null,
            _ => rules.UnknownProbeAfter is { } after ? hold.ObservedAt + after : null,
        };
        if (hold.ReleasedAt is not { } released) return natural;
        return natural is { } n && n < released ? n : released;
    }

    public static AgentHoldState Phase(HoldRow hold, DateTimeOffset now, PoolRules rules)
    {
        if (hold.LeaseUntil is { } lease && lease > now) return AgentHoldState.Attempting;
        return ReleaseTime(hold, rules) is { } at && at <= now ? AgentHoldState.Ready : AgentHoldState.Blocked;
    }

    public static SeatAdmission Admit(
        ImmutableDictionary<string, HoldRow> holds, IReadOnlyList<string> covering, DateTimeOffset now, PoolRules rules)
    {
        var probing = new List<HoldRow>();
        var ready = now;
        var blocked = false;
        var hostOnly = false;
        foreach (var hold in holds.Values)
        {
            if (!covering.Contains(hold.Scope, StringComparer.Ordinal)) continue;
            switch (Phase(hold, now, rules))
            {
                case AgentHoldState.Ready:
                    probing.Add(hold);
                    break;
                case AgentHoldState.Attempting:
                    blocked = true;
                    ready = Max(ready, hold.LeaseUntil!.Value);
                    break;
                default:
                    blocked = true;
                    if (ReleaseTime(hold, rules) is { } at) ready = Max(ready, at);
                    else hostOnly = true;
                    break;
            }
        }

        return blocked ? new SeatAdmission(false, [], hostOnly ? null : ready) : new SeatAdmission(true, probing, null);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    // ── events ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>An attempt takes the seat: each probing hold is leased to <paramref name="token"/>.</summary>
    public static ImmutableDictionary<string, HoldRow> Lease(
        ImmutableDictionary<string, HoldRow> holds, IEnumerable<HoldRow> probing, string token, DateTimeOffset now,
        PoolRules rules)
    {
        foreach (var hold in probing)
            holds = holds.SetItem(hold.Id, hold with { LeaseUntil = now + rules.AttemptLease, LeaseToken = token });
        return holds;
    }

    /// <summary>The attempt ended without evidence either way (abandoned, or a failure that restricted nothing):
    /// its leases go, and the holds stay released.</summary>
    public static ImmutableDictionary<string, HoldRow> Unlease(ImmutableDictionary<string, HoldRow> holds, string token)
    {
        foreach (var hold in holds.Values.Where(h => h.LeaseToken == token).ToList())
            holds = holds.SetItem(hold.Id, hold with { LeaseUntil = null, LeaseToken = null });
        return holds;
    }

    /// <summary>The attempt succeeded: a hold it was admitted under is removed — only if no newer failure has
    /// restated it since (its revision is the one the attempt saw).</summary>
    public static ImmutableDictionary<string, HoldRow> Succeed(
        ImmutableDictionary<string, HoldRow> holds, IEnumerable<(string Id, long Revision)> leases, string token)
    {
        foreach (var (id, revision) in leases)
            if (holds.TryGetValue(id, out var hold) && hold.Revision == revision) holds = holds.Remove(id);
        return Unlease(holds, token);
    }

    /// <summary>A failure restates the hold of its scope and cause: a new one at revision 1, or the same one
    /// one revision up, with its recovery replaced and any release or attempt dropped.</summary>
    public static ImmutableDictionary<string, HoldRow> Hold(
        ImmutableDictionary<string, HoldRow> holds, string scope, AgentRestriction restriction, DateTimeOffset now)
    {
        var id = scope + "|" + restriction.Cause.ToString().ToLowerInvariant();
        var revision = holds.TryGetValue(id, out var old) ? old.Revision + 1 : 1;
        return holds.SetItem(id, new HoldRow(id, revision, scope, restriction, now, null, null, null));
    }

    /// <summary>The host clears a hold it names by id and revision: it becomes released (one attempt, not free
    /// passage). A revision that is not the hold's current one — a newer failure has restated it — clears nothing.</summary>
    public static (ImmutableDictionary<string, HoldRow> Holds, bool Cleared) Clear(
        ImmutableDictionary<string, HoldRow> holds, string id, long revision, DateTimeOffset now)
    {
        if (!holds.TryGetValue(id, out var hold) || hold.Revision != revision) return (holds, false);
        return (hold.ReleasedAt is null ? holds.SetItem(id, hold with { ReleasedAt = now }) : holds, true);
    }

    /// <summary>A reading of an account's allowance that shows room lifts the stated-recovery holds on that account
    /// (or the allowance it draws on) — but only holds seen BEFORE it was measured. A reading that is not strictly
    /// newer than a hold, or says nothing (<paramref name="hasRoom"/> null) or no room, changes nothing, and an
    /// owner action or an unknown recovery is never lifted by one.</summary>
    public static (ImmutableDictionary<string, HoldRow> Holds, int Lifted) Lift(
        ImmutableDictionary<string, HoldRow> holds, IReadOnlyList<string> scopes, DateTimeOffset measuredAt, bool? hasRoom)
    {
        if (hasRoom != true) return (holds, 0);
        var lifted = 0;
        foreach (var hold in holds.Values.ToList())
        {
            if (!scopes.Contains(hold.Scope, StringComparer.Ordinal)) continue;
            if (hold.Restriction.Recovery.Kind is not (RecoveryKind.ResetAt or RecoveryKind.RetryNotBefore)) continue;
            if (measuredAt <= hold.ObservedAt) continue;
            holds = holds.Remove(hold.Id);
            lifted++;
        }
        return (holds, lifted);
    }

    /// <summary>What a failed turn restricts, and where. A limit or an access failure restricts something even when
    /// the door stated nothing more (an access failure the account, a bare limit the exact seat that met it); a
    /// transient or fatal one only when it carries a restriction. Null = nothing to hold.</summary>
    public static (string Scope, AgentRestriction Restriction)? FailureHold(
        AgentFailure failure, AgentAccount account, string? model, string? allowance)
    {
        var restriction = failure.Restriction;
        if (restriction is null)
        {
            restriction = failure.Kind switch
            {
                AgentFailureKind.Limit => new AgentRestriction(
                    RestrictionCause.Unknown, RestrictionSubject.Unknown, RestrictionRecovery.Unknown),
                AgentFailureKind.Access => new AgentRestriction(
                    RestrictionCause.Unknown, RestrictionSubject.Account, RestrictionRecovery.Unknown),
                _ => null,
            };
            if (restriction is null) return null;
        }

        var subject = restriction.Subject == RestrictionSubject.Unknown && failure.Kind == AgentFailureKind.Access
            ? RestrictionSubject.Account
            : restriction.Subject;
        var scope = subject switch
        {
            RestrictionSubject.Account => AccountScope(account.Provider, account.Name),
            RestrictionSubject.Endpoint => EndpointScope(account.Provider),
            // a pool no door has shown the extent of is held only where it was seen; the model and unknown
            // subjects hold exactly the account-and-model that met them
            RestrictionSubject.Pool when allowance is not null => AllowanceScope(allowance),
            RestrictionSubject.Pool => AccountScope(account.Provider, account.Name),
            _ => SeatScope(account.Provider, account.Name, model),
        };
        return (scope, restriction);
    }
}
