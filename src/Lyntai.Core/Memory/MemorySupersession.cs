namespace Lyntai.Memory;

/// <summary>An entry a supersession ends, and the revision its approver read.</summary>
/// <param name="Entry">The predecessor, of the engine and task the successor is written to.</param>
/// <param name="ExpectedRevision">Its <see cref="MemoryItem.Revision"/> when it was reviewed; required, because a supersession
/// that ends an entry on a review it never had is the failure the guard exists for.</param>
public sealed record MemorySupersededEntry(MemoryRef Entry, string ExpectedRevision);

/// <summary>A successor and the entries it replaces, applied as one change.</summary>
/// <param name="Successor">The entry to store. It states no <see cref="MemoryWrite.Supersedes"/>, relation or key of its own: the
/// predecessors, and the key, are this record's.</param>
/// <param name="Predecessors">What it ends, each with the revision read; at least one.</param>
/// <param name="Reason">Why they ended — required, read back as <see cref="MemoryValidity.EndedReason"/> on each.</param>
public sealed record MemorySupersession(
    MemoryWrite Successor, IReadOnlyList<MemorySupersededEntry> Predecessors, string Reason)
{
    /// <summary>The caller's key; the supersession is one receipt. Null applies it without one.</summary>
    public string? OperationKey { get; init; }
}

/// <summary>What became of one predecessor when a supersession was applied.</summary>
public enum MemoryPredecessorOutcome
{
    /// <summary>It is ended by the successor.</summary>
    Ended,
    /// <summary>It was fine — at the revision read, with no other successor — and was left as it was because another predecessor
    /// failed.</summary>
    Unmoved,
    /// <summary>It is not at the revision read; it needs a fresh review.</summary>
    Changed,
    /// <summary>It already has a different live successor, or would end before it began.</summary>
    Refused,
    /// <summary>The store holds no such entry under that engine.</summary>
    Missing,
}

/// <summary>One predecessor's outcome and the revision found.</summary>
/// <param name="Entry">The predecessor.</param>
/// <param name="Outcome">What became of it.</param>
/// <param name="Revision">Its revision after the call (the one found, for <see cref="MemoryPredecessorOutcome.Changed"/>), or null
/// for <see cref="MemoryPredecessorOutcome.Missing"/>.</param>
public readonly record struct MemoryPredecessorResult(MemoryRef Entry, MemoryPredecessorOutcome Outcome, string? Revision);

/// <summary>What a supersession did: either every predecessor ended and the successor is current, or nothing changed and
/// <paramref name="Predecessors"/> says which moved.</summary>
/// <param name="Applied">Whether the successor was stored and every predecessor ended.</param>
/// <param name="Successor">The stored successor — null when nothing was applied.</param>
/// <param name="Revision">The successor's revision, or null.</param>
/// <param name="Predecessors">One result per predecessor, in the order named.</param>
public sealed record MemorySupersessionResult(
    bool Applied, MemoryRef? Successor, string? Revision, IReadOnlyList<MemoryPredecessorResult> Predecessors)
{
    /// <summary>The tiers that took the successor, as <see cref="MemoryWriteResult.Ran"/>; <see cref="MemorySources.None"/> when
    /// nothing was applied or the call was a repeat.</summary>
    public MemorySources Ran { get; init; }

    /// <summary>The key's receipt when the call carried one and took effect (or was a repeat); null otherwise.</summary>
    public MemoryReceipt? Receipt { get; init; }
}

/// <summary>OPTIONAL capability: an engine that replaces entries in one change. One call stores the successor and ends every
/// named predecessor, each guarded by the revision its approver read, in one store transaction: afterwards the successor is
/// current and every predecessor ended by it, or nothing changed (<c>docs/DECISIONS.md</c> D242).
/// <para>The embed and the annotation precede the transaction; the similarity index and the subject links follow it, are derived,
/// and a failure there leaves the supersession applied. It never falls back to storing then ending: an engine over a store that
/// cannot make the pair atomic refuses. <see cref="MemoryChangeGate"/>'s agent path is unchanged — an agent's observation is its
/// own, and this is for a host that holds the successor and calls after approval.</para></summary>
public interface ISupersedingMemory
{
    /// <summary>Whether a supersession would be applied atomically. A graph engine reports whether its store implements
    /// <see cref="ISupersedingMemoryGraphStore"/>.</summary>
    bool HoldsSupersession { get; }

    /// <summary>Store the successor and end every predecessor, or change nothing.</summary>
    /// <param name="supersession">The successor, the predecessors with their revisions, the reason and the key.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <exception cref="ArgumentException">A blank reason, no predecessor, a predecessor outside the engine or task, the successor
    /// itself, or a successor that states what this record owns.</exception>
    /// <exception cref="InvalidOperationException">A predecessor already has a different live successor; nothing was written.</exception>
    /// <exception cref="MemoryKeyConflictException">The key answered a different call.</exception>
    /// <exception cref="NotSupportedException">The store cannot apply the pair atomically, or hold the key.</exception>
    Task<MemorySupersessionResult> SupersedeAsync(MemorySupersession supersession, CancellationToken ct = default);
}
