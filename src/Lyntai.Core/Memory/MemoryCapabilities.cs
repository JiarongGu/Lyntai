namespace Lyntai.Memory;

/// <summary>An engine whose entries are connected, so one can be expanded into its detail and its
/// neighbours — the "index first, depth on demand" half of progressive retrieval.
/// <para>A composite ALWAYS implements this and routes by <see cref="MemoryRef.Engine"/> to the owning
/// member. Where that member does not implement it, expansion fails OPEN: the entry comes back with no
/// neighbours, matching how every other recall-shaped read in this library degrades.</para></summary>
public interface IExpandableMemory
{
    /// <summary>The entry's full content plus its neighbours, ordered by connection strength.</summary>
    /// <param name="reference">The entry to expand.</param>
    /// <param name="hops">How far to walk from it.</param>
    /// <param name="charBudget">Maximum characters to return; null takes the engine's configured budget.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        CancellationToken ct = default);
}

/// <summary>An engine whose entries can be linked explicitly, so an application can assert structure the
/// library could not infer.
/// <para>A composite ALWAYS implements this and routes by <see cref="MemoryRef.Engine"/>. Where the owning
/// member does not implement it, linking THROWS — unlike expansion, because a silently dropped write is
/// worse than a visible failure.</para></summary>
public interface ILinkableMemory
{
    /// <summary>Connect two entries. Directed unless <paramref name="symmetric"/>.</summary>
    /// <param name="from">The source entry.</param>
    /// <param name="to">The target entry.</param>
    /// <param name="kind">Optional relation name; null is an untyped association.</param>
    /// <param name="weight">Connection strength.</param>
    /// <param name="symmetric">Write the reverse edge too.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task LinkAsync(MemoryRef from, MemoryRef to, string? kind = null, double weight = 1.0,
        bool symmetric = false, CancellationToken ct = default);
}

/// <summary>An engine that can reap entries. Reaping is always EXPLICIT — nothing in this library deletes
/// remembered material as a side effect of decay, which only ever affects ranking.</summary>
public interface IForgettableMemory
{
    /// <summary>Reap entries, returning how many were removed.</summary>
    /// <param name="taskKey">The task to reap within.</param>
    /// <param name="scope">Optional scope filter; null reaps across the task's scopes.</param>
    /// <param name="minRetrievability">Reap entries below this retrievability; null ignores it.</param>
    /// <param name="olderThan">Reap entries older than this; null ignores it.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default);
}
