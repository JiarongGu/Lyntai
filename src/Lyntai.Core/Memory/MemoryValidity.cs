namespace Lyntai.Memory;

/// <summary>When a fact held in the WORLD — its valid time, a different axis from when the engine learned it
/// (<see cref="GraphNode.CreatedAt"/>) and from how well it is remembered. An interval, half-open:
/// <c>[<see cref="From"/>, <see cref="End"/>)</c>, so a fact is valid AT <see cref="From"/> and no longer valid
/// AT its end. A null bound is unbounded, so an entry carrying none is untimed and valid at every instant — which
/// is what every entry written before valid time existed reads as.
/// <para>What a writer STATED is <see cref="From"/> and <see cref="To"/>; <see cref="SupersededAt"/> is what a
/// later write that named this entry as its predecessor recorded. The two are kept apart so an entry's own
/// claim is never overwritten by being superseded, and <see cref="End"/> is the earlier of them.</para></summary>
/// <param name="From">When it started holding; null is since always.</param>
/// <param name="To">When the writer said it stopped holding; null is not stated.</param>
/// <param name="SupersededAt">When a successor took over; null while nothing has superseded it.</param>
/// <param name="SupersededBy">The successor, or null where none is named (an import of a file that did not hold
/// it). After the successor is removed this still names it, and the entry stays ended: reading it back from the
/// engine finds nothing at that reference.</param>
public sealed record MemoryValidity(
    DateTimeOffset? From = null, DateTimeOffset? To = null, DateTimeOffset? SupersededAt = null,
    MemoryRef? SupersededBy = null)
{
    /// <summary>Why the entry was ended through <see cref="IEndableMemory.EndAsync"/>, as its writer or confirmer
    /// stated it; null for an entry nothing ended that way, including one a write superseded.</summary>
    public string? EndedReason { get; init; }

    /// <summary>Where the interval ends: the earlier of <see cref="To"/> and <see cref="SupersededAt"/>, or null
    /// while neither is set.</summary>
    public DateTimeOffset? End => To is { } to && SupersededAt is { } at ? (to < at ? to : at) : To ?? SupersededAt;

    /// <summary>Whether the fact holds at <paramref name="at"/>: at or after <see cref="From"/>, and strictly
    /// before <see cref="End"/>.</summary>
    /// <param name="at">The instant asked about.</param>
    public bool IsValidAt(DateTimeOffset at) => (From is not { } from || from <= at) && (End is not { } end || at < end);
}

/// <summary>Which entries a read admits by their valid time (<see cref="MemoryQuery.Validity"/>). The default
/// is <see cref="Current"/>, so a store holding nothing timed reads exactly as it always did.
/// <para><b>It is a filter on explicit facts, never a resolver.</b> Two entries valid at one instant are both
/// returned: the engine does not decide which of two claims is true, and never infers that they conflict from
/// their text.</para></summary>
public readonly record struct MemoryValidityReading
{
    /// <summary>Entries valid now, by the engine's clock — the default.</summary>
    public static MemoryValidityReading Current => default;

    /// <summary>Entries that were valid at <paramref name="at"/>: a historical reading.</summary>
    /// <param name="at">The instant to read at.</param>
    public static MemoryValidityReading AsOf(DateTimeOffset at) => new() { At = at };

    /// <summary>Every entry whatever its validity, each carrying its interval so the caller can tell — the
    /// ledger, including what has been superseded and what has not started.</summary>
    public static MemoryValidityReading History => new() { IsHistory = true };

    /// <summary>The instant read at; null is now. Meaningless with <see cref="IsHistory"/>.</summary>
    public DateTimeOffset? At { get; init; }

    /// <summary>Whether every entry is admitted.</summary>
    public bool IsHistory { get; init; }

    /// <summary>Whether a read at <paramref name="now"/> admits an entry with <paramref name="validity"/>; an
    /// untimed entry (null) is admitted by every reading.</summary>
    /// <param name="validity">The entry's interval, or null when it carries none.</param>
    /// <param name="now">The engine's clock, used when <see cref="At"/> is null.</param>
    public bool Admits(MemoryValidity? validity, DateTimeOffset now) =>
        IsHistory || validity is null || validity.IsValidAt(At ?? now);

    /// <summary>Why a reading cannot be asked: history carries no instant.</summary>
    internal void ThrowIfIncoherent()
    {
        if (IsHistory && At is not null)
            throw new ArgumentException(
                $"{nameof(MemoryValidityReading)} cannot be both history and as-of an instant: history admits every entry.");
    }
}

/// <summary>The refusal an engine that holds no valid time gives a write that states some — one wording, so the
/// lexical, semantic and curated engines cannot drift into three.</summary>
internal static class MemoryValidityRefusal
{
    public static void ThrowIfStated(MemoryWrite write, string engine)
    {
        if (write.StatesValidity)
            throw new NotSupportedException(
                $"Memory engine '{engine}' cannot hold valid time, so the write would be stored without it and an ended " +
                "fact would be returned as current. Route it to a graph engine, or add one to the composite.");
    }
}

/// <summary>One entry ended by another, for <see cref="ITemporalMemoryGraphStore.SupersedeAsync"/>.</summary>
/// <param name="Superseded">The entry that ended.</param>
/// <param name="By">The successor, or null when recording an end whose successor is not held.</param>
/// <param name="At">When the successor took over — the successor's start.</param>
public readonly record struct GraphSupersession(long Superseded, long? By, DateTimeOffset At);

/// <summary>OPTIONAL half of <see cref="IMemoryGraphStore"/> that holds valid time: the entry's interval and who
/// superseded it. Without it the engine REFUSES a write that states validity or names a predecessor, rather than
/// store it without — an ended fact stored as open-ended would be returned as current.
/// <para><b>Separate rather than a member of <see cref="IMemoryGraphStore"/></b>, as
/// <see cref="IPortableMemoryGraphStore"/> is: a required member breaks every store an application wrote, and a
/// store that never heard of valid time reads every node as untimed, which is correct. All four shipped stores
/// implement it. Implementing it owes the four <see cref="GraphNode"/> members, the three
/// <see cref="GraphNodeWrite"/> inputs and the three <see cref="GraphNodeImport"/> ones, round-tripped through
/// <see cref="IPortableMemoryGraphStore"/> where that is implemented too.</para></summary>
public interface ITemporalMemoryGraphStore : IMemoryGraphStore
{
    /// <summary>Record that each named entry ended, and by what, returning how many were written. Touches
    /// nothing but those fields: no age, stability, recall count or position moves, so ending an entry is not a
    /// reinforcement and does not bury it — it is still returned by a <see cref="MemoryValidityReading.History"/>
    /// read.
    /// <para>Per entry: one this engine does not hold writes nothing; one with no successor yet takes
    /// (<see cref="GraphSupersession.By"/>, <see cref="GraphSupersession.At"/>); one already superseded BY THE
    /// SAME successor restates <c>At</c>; one whose end was recorded without a successor takes the successor and
    /// restates <c>At</c>; and one superseded by a DIFFERENT successor that still exists is left as it is and not
    /// counted — an entry has one successor, and the caller learns that from the count. A successor that was
    /// removed counts as none. A null <c>By</c> never overwrites a successor.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="supersessions">The ends to record; empty writes nothing.</param>
    /// <param name="ct">Cancellation.</param>
    Task<int> SupersedeAsync(string engine, IReadOnlyList<GraphSupersession> supersessions,
        CancellationToken ct = default);
}

/// <summary>One entry ended by <see cref="IEndableMemoryGraphStore.EndAsync"/>.</summary>
/// <param name="Id">The entry.</param>
/// <param name="Reason">Why it ended; required.</param>
/// <param name="At">When it ended — the successor's start where there is one.</param>
/// <param name="By">The successor, or null for an end that names none.</param>
/// <param name="ExpectedRevision">The revision the confirmer read, or null for an unconditional end.</param>
public sealed record GraphEnd(long Id, string Reason, DateTimeOffset At, long? By = null, long? ExpectedRevision = null);

/// <summary>What <see cref="IEndableMemoryGraphStore.EndAsync"/> did.</summary>
public enum GraphEndOutcome
{
    /// <summary>The entry is now ended.</summary>
    Ended,
    /// <summary>An end through this call was already recorded; nothing changed, and the first reason stands.</summary>
    AlreadyEnded,
    /// <summary>The entry's revision is not the expected one; nothing changed.</summary>
    Changed,
    /// <summary>The entry already has a different live successor; nothing changed.</summary>
    Refused,
    /// <summary>The store holds no such entry under that engine.</summary>
    Missing,
}

/// <summary>The outcome of one end, and the entry's revision after it (for <see cref="GraphEndOutcome.Changed"/>, the
/// one found).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Revision">The entry's revision, or null for <see cref="GraphEndOutcome.Missing"/>.</param>
public readonly record struct GraphEndResult(GraphEndOutcome Outcome, long? Revision);

/// <summary>OPTIONAL half of <see cref="ITemporalMemoryGraphStore"/> that ends ONE entry with a reason, conditionally.
/// <para><b>Separate rather than a member</b>, as its parent is: a required member breaks every store an application
/// wrote. A store that does not hold it makes the engine refuse an end rather than record one it cannot guard.</para>
/// <para>Implementing it owes <see cref="GraphNode.EndedReason"/> and <see cref="GraphNode.Revision"/>, a revision that
/// advances on <see cref="ITemporalMemoryGraphStore.SupersedeAsync"/>, on a write that changes the entry and on an
/// end — never on a touch, a link or a recall — and the portable round trip of the reason.</para></summary>
public interface IEndableMemoryGraphStore : ITemporalMemoryGraphStore
{
    /// <summary>End one entry, only if its revision is still <see cref="GraphEnd.ExpectedRevision"/> when that is
    /// given, as ONE conditional statement (or under the store's lock), so a rewrite between the read and the end
    /// cannot be ended on a review it never had.
    /// <para>Touches the end fields and the revision and no decay state. With no successor the entry's own
    /// <see cref="GraphNode.ValidTo"/> becomes the earlier of what it held and <see cref="GraphEnd.At"/>; with one it
    /// takes <see cref="ITemporalMemoryGraphStore.SupersedeAsync"/>'s rule, including that a different live successor
    /// refuses. An entry that already carries a reason is <see cref="GraphEndOutcome.AlreadyEnded"/>.</para></summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="end">The end.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GraphEndResult> EndAsync(string engine, GraphEnd end, CancellationToken ct = default);
}

/// <summary>Record that a host's check held, for <see cref="IConfirmableMemoryGraphStore.ConfirmAsync"/>.</summary>
/// <param name="Id">The entry.</param>
/// <param name="At">When the check held.</param>
/// <param name="ExpectedRevision">The revision the host read, or null for an unconditional stamp.</param>
public sealed record GraphConfirm(long Id, DateTimeOffset At, long? ExpectedRevision = null);

/// <summary>What <see cref="IConfirmableMemoryGraphStore.ConfirmAsync"/> did.</summary>
public enum GraphConfirmOutcome
{
    /// <summary>The stamp moved forward to the instant.</summary>
    Stamped,
    /// <summary>The entry already carries a stamp at that instant or later; nothing changed.</summary>
    Unchanged,
    /// <summary>The entry is not at the expected revision; nothing changed.</summary>
    Changed,
    /// <summary>The entry has been ended with a reason; nothing changed.</summary>
    Ended,
    /// <summary>The store holds no such entry under that engine.</summary>
    Missing,
}

/// <summary>The outcome of one confirmation, and the entry's revision after it (for <see cref="GraphConfirmOutcome.Changed"/>, the one found).</summary>
/// <param name="Outcome">What happened.</param>
/// <param name="Revision">The entry's revision, or null for <see cref="GraphConfirmOutcome.Missing"/>.</param>
public readonly record struct GraphConfirmResult(GraphConfirmOutcome Outcome, long? Revision);

/// <summary>OPTIONAL half of <see cref="IEndableMemoryGraphStore"/> that holds a host's re-check text and the instant its check
/// last held (<c>docs/DECISIONS.md</c> D240). The store never reads the text and runs nothing.
/// <para>Implementing it owes <see cref="GraphNode.Recheck"/> and <see cref="GraphNode.ConfirmedAt"/>, a stamp that never moves
/// backwards and is a revision change but not a reinforcement, a re-remember that keeps the stamp and keeps the stored text when it
/// names none, and the portable round trip of both.</para></summary>
public interface IConfirmableMemoryGraphStore : IEndableMemoryGraphStore
{
    /// <summary>Stamp the entry as confirmed at <see cref="GraphConfirm.At"/>, only if it is still at the expected revision when one
    /// is given. Touches the stamp and the revision and no decay state.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="confirm">The confirmation.</param>
    /// <param name="ct">Cancellation.</param>
    Task<GraphConfirmResult> ConfirmAsync(string engine, GraphConfirm confirm, CancellationToken ct = default);

    /// <summary>The entries of (<paramref name="engine"/>, <paramref name="taskKey"/>, <paramref name="scope"/>) that carry a re-check,
    /// are valid at <paramref name="now"/>, and were last confirmed before <paramref name="notConfirmedSince"/> or never — never
    /// confirmed first, then longest unconfirmed, at most <paramref name="limit"/>. Reads without writing.</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">The task to read within.</param>
    /// <param name="scope">The scope, or null for every scope of the task.</param>
    /// <param name="notConfirmedSince">The instant a confirmation must be before to be due.</param>
    /// <param name="now">The instant liveness is judged at.</param>
    /// <param name="limit">The most entries; zero or less returns none.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<GraphNode>> DueAsync(string engine, string taskKey, string? scope, DateTimeOffset notConfirmedSince,
        DateTimeOffset now, int limit, CancellationToken ct = default);
}
