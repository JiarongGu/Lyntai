using System.Collections.Immutable;

namespace Lyntai.Agents;

/// <summary>The restriction rules, as pure functions from a snapshot, the caller's clock and an event to the next
/// snapshot and an outcome. It owns no clock, no store and no lock: a host that keeps its own store reads a
/// <see cref="AgentPoolSnapshot"/>, applies one operation and writes the result under its own serialisation, and
/// <see cref="AgentAccountPool"/> is the same operations over an in-process snapshot.
/// <para><b>The rules.</b> Holds are a set, and a seat is permitted only if EVERY hold covering it permits. A hold
/// is blocked until its release moment (a reset or retry-not-before time; an unknown recovery only when the timing
/// sets <see cref="AgentPoolTiming.UnknownProbeAfter"/>; an owner action never) or until it is cleared by id and
/// revision. Released is not recovered: a released hold permits ONE attempt at a time, leased for
/// <see cref="AgentPoolTiming.AttemptLease"/>; that attempt's success removes the hold it was admitted under, and
/// its failure restates the hold at a higher revision. A newer failure is never undone by older evidence — a
/// success, a clear or an evidence observation names the revision it is about, and one that does not match the hold's
/// is ignored. A lease is resolved only by the token that owns it. A hold's identity is never reused: a hold recreated
/// under a removed id continues from the highest revision that id reached.</para></summary>
public static class AgentPoolReducer
{
    // ── scopes ───────────────────────────────────────────────────────────────────────────────────

    private static string Esc(string part) => Uri.EscapeDataString(part);

    /// <summary>The key of an account, shared by every provider that files under it.</summary>
    public static string AccountScope(string provider, string account) => $"account|{Esc(provider)}|{Esc(account)}";

    /// <summary>The key of one model on one account; a null model (the backend's default) and an empty one differ.</summary>
    public static string SeatScope(string provider, string account, string? model) =>
        $"seat|{Esc(provider)}|{Esc(account)}|{(model is null ? "-" : "+" + Esc(model))}";

    /// <summary>The key of one provider's endpoint.</summary>
    public static string EndpointScope(string provider) => $"endpoint|{Esc(provider)}";

    /// <summary>The key of an allowance several accounts draw on.</summary>
    public static string AllowanceScope(string allowance) => $"allowance|{Esc(allowance)}";

    /// <summary>Every scope that holds a seat: its account, its model on that account, its provider's endpoint and,
    /// where a door supplied one, the allowance its account draws on.</summary>
    /// <param name="account">The seat's account.</param>
    /// <param name="provider">The provider the seat runs on.</param>
    /// <param name="model">The seat's model, or null for the backend's default.</param>
    /// <param name="allowance">The allowance the account draws on, when a door showed one.</param>
    public static IReadOnlyList<string> Covering(AgentAccount account, string provider, string? model, string? allowance)
    {
        ArgumentNullException.ThrowIfNull(account);
        var scopes = new List<string>(4)
        {
            AccountScope(account.Provider, account.Name),
            SeatScope(account.Provider, account.Name, model),
            EndpointScope(provider),
        };
        if (allowance is not null) scopes.Add(AllowanceScope(allowance));
        return scopes;
    }

    /// <summary>A scope in words for a log or a screen.</summary>
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
    public static DateTimeOffset? ReleaseTime(AgentHoldEntry hold, AgentPoolTiming timing)
    {
        ArgumentNullException.ThrowIfNull(hold);
        DateTimeOffset? natural = hold.Restriction.Recovery switch
        {
            { Kind: RecoveryKind.ResetAt or RecoveryKind.RetryNotBefore, At: { } at } => at,
            { Kind: RecoveryKind.OwnerAction } => null,
            _ => timing.UnknownProbeAfter is { } after ? hold.ObservedAt + after : null,
        };
        if (hold.ReleasedAt is not { } released) return natural;
        return natural is { } n && n < released ? n : released;
    }

    /// <summary>Whether the hold is in the way at <paramref name="now"/>.</summary>
    public static AgentHoldState Phase(AgentHoldEntry hold, DateTimeOffset now, AgentPoolTiming timing)
    {
        ArgumentNullException.ThrowIfNull(hold);
        if (hold.LeaseUntil is { } lease && lease > now) return AgentHoldState.Attempting;
        return ReleaseTime(hold, timing) is { } at && at <= now ? AgentHoldState.Ready : AgentHoldState.Blocked;
    }

    /// <summary>Whether a seat covered by <paramref name="covering"/> is permitted at <paramref name="now"/>. It reads
    /// the snapshot and changes nothing: <see cref="TakeSeat"/> takes the seat.</summary>
    public static AgentSeatAdmission Admit(
        AgentPoolSnapshot snapshot, IReadOnlyList<string> covering, DateTimeOffset now, AgentPoolTiming timing)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(covering);
        var probing = new List<AgentHoldEntry>();
        var ready = now;
        var blocked = false;
        var hostOnly = false;
        foreach (var hold in snapshot.HoldMap.Values)
        {
            if (!covering.Contains(hold.Scope, StringComparer.Ordinal)) continue;
            switch (Phase(hold, now, timing))
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
                    if (ReleaseTime(hold, timing) is { } at) ready = Max(ready, at);
                    else hostOnly = true;
                    break;
            }
        }

        return blocked ? new AgentSeatAdmission(false, [], hostOnly ? null : ready) : new AgentSeatAdmission(true, probing, null);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a >= b ? a : b;

    // ── events ───────────────────────────────────────────────────────────────────────────────────

    /// <summary>An attempt takes a seat against the whole applicable state: the seat is admitted from the snapshot
    /// as it stands and, when permitted, each released hold covering it is leased to <paramref name="token"/>. A seat a
    /// hold blocks is not taken and the snapshot is returned unchanged.</summary>
    /// <param name="snapshot">The state the seat is taken from.</param>
    /// <param name="covering">The seat's scopes (<see cref="Covering"/>).</param>
    /// <param name="token">Identifies the attempt; unique per attempt.</param>
    /// <param name="now">The caller's clock.</param>
    /// <param name="timing">The bounds.</param>
    public static AgentPoolStep<AgentSeatTaken> TakeSeat(
        AgentPoolSnapshot snapshot, IReadOnlyList<string> covering, string token, DateTimeOffset now, AgentPoolTiming timing)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        var admission = Admit(snapshot, covering, now, timing);
        if (!admission.Permitted) return new(snapshot, new AgentSeatTaken(false, [], admission.ReadyAt));

        var holds = snapshot.HoldMap;
        foreach (var hold in admission.Probing)
            holds = holds.SetItem(hold.Id, hold with { LeaseUntil = now + timing.AttemptLease, LeaseToken = token });
        return new(snapshot.WithHolds(holds),
            new AgentSeatTaken(true, [.. admission.Probing.Select(h => new AgentLease(h.Id, h.Revision))], null));
    }

    /// <summary>The attempt ended without evidence either way (abandoned, or a failure that restricted nothing): the
    /// leases <paramref name="token"/> owns go, and the holds stay released. A token that owns none changes nothing.
    /// The outcome is how many leases went.</summary>
    public static AgentPoolStep<int> Abandon(AgentPoolSnapshot snapshot, string token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var holds = snapshot.HoldMap;
        var released = 0;
        foreach (var hold in holds.Values.Where(h => h.LeaseToken == token).ToList())
        {
            holds = holds.SetItem(hold.Id, hold with { LeaseUntil = null, LeaseToken = null });
            released++;
        }
        return new(snapshot.WithHolds(holds), released);
    }

    /// <summary>The attempt succeeded: a hold it was admitted under is removed — only if no newer failure has restated
    /// it since (its revision is the one the attempt saw) and <paramref name="token"/> still owns its lease, which
    /// another attempt took once this one's ran out. The attempt's own leases go either way. The outcome is the ids removed.</summary>
    public static AgentPoolStep<IReadOnlyList<string>> Succeed(
        AgentPoolSnapshot snapshot, IEnumerable<AgentLease> leases, string token)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(leases);
        var removed = new List<string>();
        foreach (var (id, revision) in leases)
        {
            if (!snapshot.HoldMap.TryGetValue(id, out var hold) || hold.Revision != revision || hold.LeaseToken != token) continue;
            snapshot = snapshot.Remove(id);
            removed.Add(id);
        }
        return new(Abandon(snapshot, token).Snapshot, removed);
    }

    /// <summary>A failure restates the hold of its scope and cause: a new one one revision above the highest this id ever
    /// reached, or the same one one revision up, with its recovery replaced and any release or attempt dropped. The outcome
    /// is the hold as it now stands.</summary>
    /// <param name="snapshot">The state.</param>
    /// <param name="scope">What the failure covers (<see cref="AccountScope"/> and its siblings).</param>
    /// <param name="restriction">What the failure stated.</param>
    /// <param name="now">The caller's clock.</param>
    public static AgentPoolStep<AgentHoldEntry> Observe(
        AgentPoolSnapshot snapshot, string scope, AgentRestriction restriction, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrEmpty(scope);
        ArgumentNullException.ThrowIfNull(restriction);
        var id = scope + "|" + restriction.Cause.ToString().ToLowerInvariant();
        var revision = Math.Max(snapshot.HoldMap.TryGetValue(id, out var old) ? old.Revision : 0, snapshot.RetiredMap.GetValueOrDefault(id)) + 1;
        var hold = new AgentHoldEntry(id, revision, scope, restriction, now, null, null, null);
        return new(snapshot.WithHolds(snapshot.HoldMap.SetItem(id, hold)), hold);
    }

    /// <summary>A failed turn observed as a hold, where it restricts something (<see cref="HoldOf"/>); the snapshot is
    /// returned unchanged, with a null outcome, where it restricts nothing.</summary>
    public static AgentPoolStep<AgentHoldEntry?> Observe(
        AgentPoolSnapshot snapshot, AgentFailure failure, AgentAccount account, string provider, string? model, string? allowance,
        DateTimeOffset now)
    {
        if (HoldOf(failure, account, provider, model, allowance) is not { } request) return new(snapshot, null);
        var step = Observe(snapshot, request.Scope, request.Restriction, now);
        return new(step.Snapshot, step.Outcome);
    }

    /// <summary>The host clears a hold it names by id and revision: it becomes released (one attempt, not free
    /// passage). A revision that is not the hold's current one — a newer failure has restated it — clears nothing. The
    /// outcome is whether it cleared.</summary>
    public static AgentPoolStep<bool> Clear(AgentPoolSnapshot snapshot, string id, long revision, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(id);
        if (!snapshot.HoldMap.TryGetValue(id, out var hold) || hold.Revision != revision) return new(snapshot, false);
        return new(hold.ReleasedAt is null ? snapshot.WithHolds(snapshot.HoldMap.SetItem(id, hold with { ReleasedAt = now })) : snapshot, true);
    }

    /// <summary>Evidence that came from no attempt discharges a hold — removes it — only when it names the hold exactly:
    /// the id, the current revision and the hold's own scope as its subject key, taken strictly after the hold was
    /// observed, and answering the hold's recovery kind. A stale revision, a scope or subject that is not the hold's, an
    /// older measurement or a kind the evidence does not answer changes nothing. The outcome is whether it discharged.</summary>
    public static AgentPoolStep<bool> Discharge(AgentPoolSnapshot snapshot, AgentPoolEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(evidence);
        if (!snapshot.HoldMap.TryGetValue(evidence.HoldId, out var hold)
            || hold.Revision != evidence.Revision
            || !string.Equals(hold.Scope, evidence.SubjectKey, StringComparison.Ordinal)
            || evidence.MeasuredAt <= hold.ObservedAt
            || evidence.Answers is null
            || !evidence.Answers.Contains(hold.Restriction.Recovery.Kind))
            return new(snapshot, false);
        return new(snapshot.Remove(hold.Id), true);
    }

    /// <summary>What a failed turn restricts, and where. A limit or an access failure restricts something even when
    /// the door stated nothing more (an access failure the account, a bare limit the exact seat that met it); a
    /// transient or fatal one only when it carries a restriction. Null = nothing to hold.</summary>
    /// <param name="failure">The failed turn's failure.</param>
    /// <param name="account">The account the turn ran on.</param>
    /// <param name="provider">The provider it ran on.</param>
    /// <param name="model">The model it ran, or null for the backend's default.</param>
    /// <param name="allowance">The allowance the account draws on, when a door showed one.</param>
    public static AgentHoldRequest? HoldOf(AgentFailure failure, AgentAccount account, string provider, string? model, string? allowance)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(account);
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
            RestrictionSubject.Endpoint => EndpointScope(provider),
            // a pool no door has shown the extent of is held only where it was seen; the model and unknown
            // subjects hold exactly the account-and-model that met them
            RestrictionSubject.Pool when allowance is not null => AllowanceScope(allowance),
            RestrictionSubject.Pool => AccountScope(account.Provider, account.Name),
            _ => SeatScope(account.Provider, account.Name, model),
        };
        return new AgentHoldRequest(scope, restriction);
    }
}
