namespace Lyntai.Memory;

/// <summary>A relation between two LIVE entries of one task that their writer states and the engine never infers.
/// Neither ends anything: both entries stay current, and a reader sees the writer's assertion beside them.</summary>
public enum MemoryRelation
{
    /// <summary>The asserting entry says the other entry's claim is wrong or in doubt.</summary>
    Disputes,
    /// <summary>The asserting entry amends the other: it corrects or extends one claim of it and leaves the rest standing.</summary>
    Amends,
}

/// <summary>One relation an entry takes part in, as a recall reports it.
/// <para><b>It is the writer's attributed assertion, never an established disagreement</b> (<c>docs/DECISIONS.md</c>
/// D240, D241): the asserting entry's own text and <see cref="MemoryItem.Metadata"/> are the evidence and the stamp, and
/// nothing here says which of the two is right.</para></summary>
/// <param name="Relation">What is asserted.</param>
/// <param name="Other">The entry at the other end; it is in the same reading, or the relation is not reported.</param>
/// <param name="AssertedByThis">Whether THIS entry is the one that asserts it; false when <paramref name="Other"/> does.</param>
public sealed record MemoryItemRelation(MemoryRelation Relation, MemoryRef Other, bool AssertedByThis);

/// <summary>OPTIONAL capability: an engine that records a relation between two of its live entries after both exist.
/// A write states one too (<see cref="MemoryWrite.Disputes"/>, <see cref="MemoryWrite.Amends"/>).
/// <para>A composite ALWAYS implements this and routes by <see cref="MemoryRef.Engine"/>; both ends must name one
/// engine. Where the owner cannot hold a relation the call throws, because a dropped assertion is invisible.</para></summary>
public interface IRelatableMemory
{
    /// <summary>Record that <paramref name="from"/> asserts <paramref name="relation"/> of <paramref name="to"/>. Repeating
    /// it changes nothing. Refuses a reference outside this engine or task, an entry that is not valid now, itself, and
    /// a store that cannot read the relation back.</summary>
    /// <param name="taskKey">The task both entries must belong to.</param>
    /// <param name="from">The asserting entry.</param>
    /// <param name="to">The entry it asserts the relation of.</param>
    /// <param name="relation">What is asserted.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <exception cref="ArgumentException">A reference that names no usable entry, or an entry related to itself.</exception>
    /// <exception cref="NotSupportedException">The store cannot hold or read a typed link.</exception>
    Task RelateAsync(string taskKey, MemoryRef from, MemoryRef to, MemoryRelation relation, CancellationToken ct = default);
}

/// <summary>One typed link between two entries, as <see cref="ITypedLinkMemoryGraphStore.LinksAmongAsync"/> reads it.</summary>
/// <param name="From">The source entry.</param>
/// <param name="To">The target entry.</param>
/// <param name="Kind">The relation name the link was written with.</param>
public readonly record struct GraphTypedLink(long From, long To, string Kind);

/// <summary>OPTIONAL half of <see cref="IMemoryGraphStore"/> that reads the links of named kinds among a set of entries.
/// <para>An edge's identity is (from, to, kind), so a typed link is its own edge that a recall's untyped co-activation
/// never strengthens; what no member of <see cref="IMemoryGraphStore"/> returns is an edge's kind or its source, which is
/// what this read adds. Implementing it owes the task boundary (an endpoint outside the task is not returned) and
/// that nothing is reinforced or written by reading.</para></summary>
public interface ITypedLinkMemoryGraphStore : IMemoryGraphStore
{
    /// <summary>The links whose kind is one of <paramref name="kinds"/> and whose BOTH ends are in
    /// <paramref name="ids"/> and in (<paramref name="engine"/>, <paramref name="taskKey"/>).</summary>
    /// <param name="engine">The owning engine's name.</param>
    /// <param name="taskKey">The task both ends must belong to.</param>
    /// <param name="ids">The entries to look among; empty returns none.</param>
    /// <param name="kinds">The kinds to return; empty returns none.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<GraphTypedLink>> LinksAmongAsync(string engine, string taskKey, IReadOnlyCollection<long> ids,
        IReadOnlyCollection<string> kinds, CancellationToken ct = default);
}
