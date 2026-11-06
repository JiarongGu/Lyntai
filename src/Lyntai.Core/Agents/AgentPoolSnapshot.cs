using System.Collections.Immutable;

namespace Lyntai.Agents;

/// <summary>One held restriction, as <see cref="AgentPoolSnapshot"/> keeps it. <see cref="Scope"/> is the canonical key of
/// what it covers (<see cref="AgentPoolReducer.AccountScope"/> and its siblings); <see cref="Id"/> adds its cause, so
/// independent causes on one scope are independent holds.</summary>
/// <param name="Id">The hold's identity: its scope and its cause. Never reused for a different failure: see
/// <see cref="AgentPoolSnapshot.RetiredRevisions"/>.</param>
/// <param name="Revision">Starts at 1 and rises each time a newer failure restates the hold, continuing from the highest
/// revision this id reached before it was removed.</param>
/// <param name="Scope">The canonical key of what it covers.</param>
/// <param name="Restriction">What the failure stated.</param>
/// <param name="ObservedAt">When the failure was taken, on the caller's clock.</param>
/// <param name="ReleasedAt">When a stated moment, a probe time or a clear released it; null while it blocks.</param>
/// <param name="LeaseUntil">When the attempt admitted under it must report, or null while none is in flight.</param>
/// <param name="LeaseToken">The token that owns that attempt.</param>
public sealed record AgentHoldEntry(
    string Id, long Revision, string Scope, AgentRestriction Restriction, DateTimeOffset ObservedAt,
    DateTimeOffset? ReleasedAt, DateTimeOffset? LeaseUntil, string? LeaseToken);

/// <summary>The state of one pool, which a host stores and whose writes it serialises: the holds, and for each id a hold
/// was removed under, the highest revision it reached. An immutable value. <see cref="AgentPoolReducer"/> maps it and
/// an event to the next one, and keeps no clock, no store and no lock of its own.
/// <para>A host that persists it writes <see cref="Holds"/> and <see cref="RetiredRevisions"/> and reads them back through
/// <see cref="Create"/>. Dropping <see cref="RetiredRevisions"/> loses the guarantee that a clear naming an older
/// revision matches nothing against a hold recreated after its removal.</para></summary>
public sealed class AgentPoolSnapshot
{
    /// <summary>A pool that holds nothing and has retired nothing.</summary>
    public static AgentPoolSnapshot Empty { get; } = new(
        ImmutableDictionary<string, AgentHoldEntry>.Empty.WithComparers(StringComparer.Ordinal),
        ImmutableDictionary<string, long>.Empty.WithComparers(StringComparer.Ordinal));

    internal AgentPoolSnapshot(ImmutableDictionary<string, AgentHoldEntry> holds, ImmutableDictionary<string, long> retired)
    {
        HoldMap = holds;
        RetiredMap = retired;
    }

    internal ImmutableDictionary<string, AgentHoldEntry> HoldMap { get; }

    internal ImmutableDictionary<string, long> RetiredMap { get; }

    /// <summary>The holds by id.</summary>
    public IReadOnlyDictionary<string, AgentHoldEntry> Holds => HoldMap;

    /// <summary>For each id a hold was removed under, the highest revision it reached: a hold recreated under that id
    /// continues from it.</summary>
    public IReadOnlyDictionary<string, long> RetiredRevisions => RetiredMap;

    /// <summary>A snapshot of the holds and retired revisions a host stored.</summary>
    /// <param name="holds">The holds; each id once.</param>
    /// <param name="retiredRevisions">The retired revisions; null = none.</param>
    /// <exception cref="ArgumentException">An id is held twice, an id or a scope is blank, or a revision is below 1.</exception>
    public static AgentPoolSnapshot Create(IEnumerable<AgentHoldEntry> holds, IReadOnlyDictionary<string, long>? retiredRevisions = null)
    {
        ArgumentNullException.ThrowIfNull(holds);
        var map = Empty.HoldMap.ToBuilder();
        foreach (var hold in holds)
        {
            ArgumentNullException.ThrowIfNull(hold);
            if (string.IsNullOrEmpty(hold.Id) || string.IsNullOrEmpty(hold.Scope))
                throw new ArgumentException("A hold needs an id and a scope.", nameof(holds));
            if (hold.Revision < 1)
                throw new ArgumentException($"Hold '{hold.Id}' has revision {hold.Revision}; a revision starts at 1.", nameof(holds));
            if (!map.TryAdd(hold.Id, hold))
                throw new ArgumentException($"Hold '{hold.Id}' is listed twice.", nameof(holds));
        }

        var retired = Empty.RetiredMap.ToBuilder();
        foreach (var (id, revision) in retiredRevisions ?? ImmutableDictionary<string, long>.Empty)
        {
            if (revision < 1) throw new ArgumentException($"Retired id '{id}' has revision {revision}.", nameof(retiredRevisions));
            retired[id] = revision;
        }
        return new AgentPoolSnapshot(map.ToImmutable(), retired.ToImmutable());
    }

    internal AgentPoolSnapshot WithHolds(ImmutableDictionary<string, AgentHoldEntry> holds) =>
        ReferenceEquals(holds, HoldMap) ? this : new AgentPoolSnapshot(holds, RetiredMap);

    internal AgentPoolSnapshot Remove(string id) =>
        HoldMap.TryGetValue(id, out var hold)
            ? new AgentPoolSnapshot(HoldMap.Remove(id), RetiredMap.SetItem(id, Math.Max(hold.Revision, RetiredMap.GetValueOrDefault(id))))
            : this;
}

/// <summary>The bounds the rules need, which <see cref="AgentPoolOptions"/> states.</summary>
/// <param name="AttemptLease">How long an attempt admitted under a released hold may run before another is admitted.</param>
/// <param name="UnknownProbeAfter">How long after its observation an unknown recovery is released, or null when only the
/// host can.</param>
public readonly record struct AgentPoolTiming(TimeSpan AttemptLease, TimeSpan? UnknownProbeAfter)
{
    /// <summary>The bounds <paramref name="options"/> states.</summary>
    public static AgentPoolTiming From(AgentPoolOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new AgentPoolTiming(options.AttemptLease, options.UnknownProbeAfter);
    }
}

/// <summary>What a rule returns beside the next snapshot.</summary>
/// <typeparam name="T">The outcome's type.</typeparam>
/// <param name="Snapshot">The state after the event; the same instance when nothing changed.</param>
/// <param name="Outcome">What the event did.</param>
public readonly record struct AgentPoolStep<T>(AgentPoolSnapshot Snapshot, T Outcome);

/// <summary>An attempt's claim on one hold: the revision it saw when it was admitted.</summary>
/// <param name="HoldId">The hold's id.</param>
/// <param name="Revision">The revision it was admitted under.</param>
public readonly record struct AgentLease(string HoldId, long Revision);

/// <summary>Whether a seat is permitted: every covering hold must be released and free of an attempt.</summary>
/// <param name="Permitted">True when no covering hold blocks.</param>
/// <param name="Probing">The covering holds that are released, so the seat is permitted for one attempt under them.</param>
/// <param name="ReadyAt">When not permitted: the latest moment among the covering holds, or null when one of them waits
/// on the host. Null when permitted.</param>
public sealed record AgentSeatAdmission(bool Permitted, IReadOnlyList<AgentHoldEntry> Probing, DateTimeOffset? ReadyAt);

/// <summary>A seat taken against the state as it stands.</summary>
/// <param name="Permitted">False when a covering hold blocks, and then nothing was leased.</param>
/// <param name="Leases">The holds the attempt is leased under, empty when the seat was free of holds.</param>
/// <param name="ReadyAt">When not permitted: as <see cref="AgentSeatAdmission.ReadyAt"/>.</param>
public sealed record AgentSeatTaken(bool Permitted, IReadOnlyList<AgentLease> Leases, DateTimeOffset? ReadyAt)
{
    /// <summary>True when the seat was taken under at least one released hold, so the attempt is a probe.</summary>
    public bool IsProbe => Permitted && Leases.Count > 0;
}

/// <summary>Evidence about one hold that came from no attempt: a reading of an allowance, a refilled balance a person
/// reported. It discharges the hold only if it names it exactly and answers how it ends.</summary>
/// <param name="HoldId">The hold's id.</param>
/// <param name="Revision">The revision the evidence was read against; a newer failure has restated the hold since if it
/// is not the current one, and the evidence is stale.</param>
/// <param name="SubjectKey">The canonical key the evidence is about, as <see cref="AgentPoolReducer.AccountScope"/> and its
/// siblings build it. It must equal the hold's <see cref="AgentHoldEntry.Scope"/>: evidence about another scope, or about
/// another account's scope, discharges nothing.</param>
/// <param name="MeasuredAt">When the evidence was taken. It must be strictly after the hold was observed.</param>
/// <param name="Answers">The recovery kinds this evidence answers, which the host names: a reading of an allowance's room
/// answers <see cref="RecoveryKind.ResetAt"/> and <see cref="RecoveryKind.RetryNotBefore"/>, and no reading answers an owner
/// action unless the host says so.</param>
public sealed record AgentPoolEvidence(
    string HoldId, long Revision, string SubjectKey, DateTimeOffset MeasuredAt, IReadOnlyCollection<RecoveryKind> Answers);

/// <summary>What a failure restricts and where: a hold to take, in the scope that covers it.</summary>
/// <param name="Scope">The canonical key of what it covers.</param>
/// <param name="Restriction">What the failure stated, or the bare restriction a limit or an access failure implies.</param>
public sealed record AgentHoldRequest(string Scope, AgentRestriction Restriction);
