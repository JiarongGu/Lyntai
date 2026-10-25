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
/// <param name="SupersededBy">The successor, or null — also null where the successor was not carried (an import
/// of a file that did not hold it) or has since been removed, which does not reopen the entry.</param>
public sealed record MemoryValidity(
    DateTimeOffset? From = null, DateTimeOffset? To = null, DateTimeOffset? SupersededAt = null,
    MemoryRef? SupersededBy = null)
{
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
/// implement it. Implementing it owes the four <see cref="GraphNode"/> members and the three
/// <see cref="GraphNodeWrite"/> inputs, round-tripped through <see cref="IPortableMemoryGraphStore"/> where
/// that is implemented too.</para></summary>
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
