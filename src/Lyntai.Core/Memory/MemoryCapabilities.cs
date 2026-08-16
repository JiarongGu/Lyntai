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

/// <summary>An engine that can remove entries. Removal is always EXPLICIT — nothing in this library deletes
/// remembered material as a side effect of decay, which only ever affects ranking.
///
/// <para><b>Both removing verbs live here, and that is what makes them reachable.</b> Through 2.5.x
/// <c>ForgetAsync</c> was a bare public method on <c>GraphMemoryEngine</c> and on no interface at all, while
/// <c>CompositeMemoryEngine</c> — which <c>MemoryEngineBuilder.Build</c> produces for EVERY registration,
/// even a single-member one — did not implement this interface. So a consumer holding the
/// <see cref="IMemoryEngine"/> that <see cref="IMemoryEngineFactory"/> hands back could reach neither: the
/// type test failed and the method was invisible. A memory subsystem you cannot delete from is not a
/// capability that was deferred, it is one that was lost behind a wrapper.</para></summary>
public interface IForgettableMemory
{
    /// <summary>Forget everything remembered under (<paramref name="taskKey"/>, <paramref name="scope"/>),
    /// unconditionally.
    /// <para>Returns no count: it removes the scope rather than a qualifying subset, so "how many" is not
    /// the question a caller is asking. It is the deletion path an application uses when a user withdraws
    /// consent or a task ends, which is why it must be reachable through the interface rather than through a
    /// concrete type.</para></summary>
    /// <param name="taskKey">The task to forget within.</param>
    /// <param name="scope">Optional scope filter; null forgets across the task's scopes. An engine whose
    /// store cannot express "every scope" must THROW rather than forget one scope or none — a consent
    /// withdrawal that silently does less than it says is the failure this whole surface exists to
    /// prevent.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default);
}

/// <summary>OPTIONAL capability: an engine that can remove a qualifying SUBSET, for capacity management.
///
/// <para><b>Split from <see cref="IForgettableMemory"/> in 3.0, because the two answer different
/// questions.</b> Forgetting is a targeted withdrawal of one user's data — it must be complete, and a
/// partial one is a broken promise. Pruning bounds an ever-growing store — it is best-effort by nature, an
/// operator's or a scheduler's act rather than a user's, and removing fewer entries than hoped is a
/// deferred cost rather than a defect.</para>
///
/// <para>They were one interface, and that forced an engine to claim both or neither. A vector store with no
/// age column can forget a scope exactly and cannot prune by age at all; under the old shape it had to either
/// lie about pruning or give up forgetting. Worse, a composite could only pre-check that members implemented
/// the interface — so a member that implemented it and threw from one of the two methods produced the exact
/// outcome the pre-check exists to prevent: some members removed, then an exception.</para></summary>
public interface IPrunableMemory
{
    /// <summary>Remove entries matching the criteria, returning how many were removed.</summary>
    /// <param name="taskKey">The task to remove within.</param>
    /// <param name="scope">Optional scope filter; null removes across the task's scopes.</param>
    /// <param name="minRetrievability">Remove entries below this retrievability; null ignores it.</param>
    /// <param name="olderThan">Remove entries older than this; null ignores it.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <remarks><b>A criterion an engine cannot EXPRESS must remove nothing rather than be ignored.</b>
    /// Ignoring <paramref name="scope"/> or <paramref name="minRetrievability"/> and removing on what is left
    /// deletes MORE than was asked for — the one direction a deletion must never err in.</remarks>
    Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default);
}
