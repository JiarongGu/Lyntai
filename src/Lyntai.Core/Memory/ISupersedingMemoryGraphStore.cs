namespace Lyntai.Memory;

/// <summary>A predecessor of a supersession and the revision its approver read.</summary>
/// <param name="Id">The entry.</param>
/// <param name="ExpectedRevision">The revision read.</param>
public sealed record GraphPredecessor(long Id, long ExpectedRevision);

/// <summary>What became of one predecessor in <see cref="ISupersedingMemoryGraphStore.SupersedeWithAsync"/>.</summary>
public enum GraphPredecessorStanding
{
    /// <summary>Ended by the successor.</summary>
    Ended,
    /// <summary>At the revision read with no other live successor, and left as it was because another predecessor failed.</summary>
    Unmoved,
    /// <summary>Not at the revision read, or already ended.</summary>
    Changed,
    /// <summary>It has a different live successor, or the successor would start before it did.</summary>
    Refused,
    /// <summary>The store holds no such entry under that engine.</summary>
    Missing,
}

/// <summary>One predecessor's standing and its revision after the call (the one found, unless it was ended).</summary>
/// <param name="Id">The entry.</param>
/// <param name="Standing">What became of it.</param>
/// <param name="Revision">Its revision, or null for <see cref="GraphPredecessorStanding.Missing"/>.</param>
public readonly record struct GraphPredecessorResult(long Id, GraphPredecessorStanding Standing, long? Revision);

/// <summary>What <see cref="ISupersedingMemoryGraphStore.SupersedeWithAsync"/> did.</summary>
/// <param name="Applied">Whether the successor was stored and every predecessor ended.</param>
/// <param name="SuccessorId">The successor's id, or null when nothing was applied.</param>
/// <param name="SuccessorRevision">Its revision after the call, or null.</param>
/// <param name="Predecessors">One result per predecessor, in the order given.</param>
/// <param name="Replayed">Whether the key had already answered; nothing was applied by this call.</param>
/// <param name="Receipt">The key's receipt, or null when none was given or nothing was applied.</param>
public sealed record GraphSupersedeResult(
    bool Applied, long? SuccessorId, long? SuccessorRevision, IReadOnlyList<GraphPredecessorResult> Predecessors,
    bool Replayed, GraphReceipt? Receipt);

/// <summary>OPTIONAL half of <see cref="IKeyedMemoryGraphStore"/> that replaces entries in ONE transaction (the in-process store
/// under its lock, the file store in one journal line): it stores the successor and ends every predecessor, or changes nothing —
/// a stale predecessor leaves the successor absent, no position advanced, no predecessor ended (<c>docs/DECISIONS.md</c> D242).
/// <para>Each predecessor is judged as <see cref="IEndableMemoryGraphStore.EndAsync"/> judges one, by the revision given, and
/// the whole result says which moved. The key's receipt, when given, commits in the same transaction and names the successor.</para></summary>
public interface ISupersedingMemoryGraphStore : IKeyedMemoryGraphStore
{
    /// <summary>Store <paramref name="successor"/> and end every predecessor by it, at the successor's own start where it holds
    /// one and else <paramref name="at"/>, each with <paramref name="reason"/>. A predecessor that would end before it began is
    /// <see cref="GraphPredecessorStanding.Refused"/>.</summary>
    /// <param name="successor">The node to store.</param>
    /// <param name="predecessors">What it ends; at least one, each id once. The successor among them is
    /// <see cref="GraphPredecessorStanding.Refused"/>, whatever revision its refresh left it at.</param>
    /// <param name="reason">Why they ended.</param>
    /// <param name="at">The end, where the successor holds no start.</param>
    /// <param name="key">The key, whose kind is <see cref="MemoryOperationKind.Supersede"/>, or null for none.</param>
    /// <param name="ct">Cancellation.</param>
    /// <exception cref="ArgumentException">No predecessor, or one named twice.</exception>
    Task<GraphSupersedeResult> SupersedeWithAsync(GraphNodeWrite successor, IReadOnlyList<GraphPredecessor> predecessors,
        string reason, DateTimeOffset at, GraphOperationKey? key, CancellationToken ct = default);
}
