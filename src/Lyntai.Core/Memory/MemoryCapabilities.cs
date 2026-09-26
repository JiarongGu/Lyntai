namespace Lyntai.Memory;

/// <summary>An engine whose entries are connected, so one can be expanded into its detail and its
/// neighbours — the "index first, depth on demand" half of progressive retrieval.
/// <para>A composite ALWAYS implements this and routes by <see cref="MemoryRef.Engine"/> to the owning
/// member. Where that member does not implement it, expansion fails OPEN: the entry comes back with no
/// neighbours, matching how every other recall-shaped read in this library degrades.</para></summary>
public interface IExpandableMemory
{
    /// <summary>The entry's full content plus its neighbours, ordered by connection strength. A reference
    /// whose <see cref="MemoryRef.Engine"/> names another engine expands to nothing: its id addresses another
    /// store.</summary>
    /// <param name="reference">The entry to expand.</param>
    /// <param name="hops">How far to walk from it.</param>
    /// <param name="charBudget">Maximum characters to return; null takes the engine's configured budget.</param>
    /// <param name="detail">How much of each NEIGHBOUR to return. The named entry always comes back whole —
    /// that is what expansion means — while its neighbours default to headlines for the same reason a recall
    /// does: they are the next things you might expand, not the thing you asked for.
    /// <para><see cref="MemoryDetail.Full"/> returns them whole too, which is what a caller walking to
    /// ANSWER wants (<c>docs/DECISIONS.md</c> D104). A walk passes its query's own value through, so asking
    /// once on the query is enough.</para></param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        MemoryDetail detail = MemoryDetail.Headline, CancellationToken ct = default);
}

/// <summary>An engine whose entries can be linked explicitly, so an application can assert structure the
/// library could not infer.
/// <para>A composite ALWAYS implements this and routes by <see cref="MemoryRef.Engine"/>. Where the owning
/// member does not implement it, linking THROWS — unlike expansion, because a silently dropped write is
/// worse than a visible failure.</para></summary>
public interface ILinkableMemory
{
    /// <summary>Connect two entries. Directed unless <paramref name="symmetric"/>. Both must belong to the
    /// engine linking them: a reference naming another engine throws <see cref="ArgumentException"/>.</summary>
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
/// <para><b>Reached by a type test</b> on the <see cref="IMemoryEngine"/> that
/// <see cref="IMemoryEngineFactory"/> hands back — which is always a <c>CompositeMemoryEngine</c>, so every
/// capability an engine has must also be declared by the blend around it, or no consumer can reach
/// it.</para></summary>
public interface IForgettableMemory
{
    /// <summary>Forget everything remembered under (<paramref name="taskKey"/>, <paramref name="scope"/>),
    /// unconditionally.
    /// <para>Returns no count: it removes the scope rather than a qualifying subset, so "how many" is not
    /// the question a caller is asking. It is the deletion path an application uses when a user withdraws
    /// consent or a task ends, which is why it must be reachable through the interface rather than through a
    /// concrete type.</para>
    /// <para><b>Complete means every PROJECTION, not only the primary store.</b> An engine that also
    /// maintains an index outside the store it reads from — a vector collection, a cache, a derived table —
    /// must clear that too, or the content survives somewhere a recall no longer reaches and a consent
    /// withdrawal has silently done less than it says. Clear the projection FIRST: a failure then leaves the
    /// primary store intact and the call retryable, where the reverse reports success over surviving
    /// content.</para></summary>
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
/// <para><b>Separate from <see cref="IForgettableMemory"/>, because the two answer different questions</b>
/// (<c>docs/DECISIONS.md</c> D72). Forgetting is a targeted withdrawal of one user's data — it must be
/// complete. Pruning bounds an ever-growing store — best-effort by nature, an operator's or a scheduler's
/// act, and removing fewer entries than hoped is a deferred cost rather than a defect. A vector store can
/// forget a scope exactly and cannot prune at all, so an engine declares each capability it has.</para></summary>
public interface IPrunableMemory
{
    /// <summary>Remove entries matching the criteria, returning how many were removed.</summary>
    /// <param name="taskKey">The task to remove within.</param>
    /// <param name="scope">Optional scope filter; null removes across the task's scopes.</param>
    /// <param name="minRetrievability">Remove entries below this retrievability; null leaves it to the engine —
    /// the graph engine applies its configured <c>GraphMemoryOptions.MinRetrievability</c>.</param>
    /// <param name="olderThan">Remove entries older than this; null ignores it.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <remarks><b>A criterion an engine cannot EXPRESS must remove nothing rather than be ignored.</b>
    /// Ignoring <paramref name="scope"/> or <paramref name="minRetrievability"/> and removing on what is left
    /// deletes MORE than was asked for — the one direction a deletion must never err in.</remarks>
    Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default);
}

/// <summary>OPTIONAL capability: an engine that re-embeds what it stores, for after its embedding model changes —
/// in place, keeping every entry's id, links and decay state, where the alternative is dropping the memory and
/// writing it all again.
///
/// <para><b>It writes nothing but vectors.</b> No entry, link, position, decay state or signal changes, and no
/// annotation or salience runs. Links an engine derived from similarity stay as the old model scored them.</para>
///
/// <para>Reached by a type test, like <see cref="IPrunableMemory"/>; the blend around an engine declares it too,
/// fanning out to the members that have it.</para></summary>
public interface IReindexableMemory
{
    /// <summary>Re-embed every entry under (<paramref name="taskKey"/>, <paramref name="scope"/>) with the engine's
    /// current embedding backend, and write each vector back where the old one was.
    /// <para><b>Pause writes to the task while it runs.</b> A write landing mid-pass is embedded by the new model,
    /// but its similarity search still meets old vectors, so under a new model of the same dimension its links may
    /// be scored against the wrong one.</para>
    /// <para>A removal landing mid-pass is safe within one process: the pass never writes a vector back for an entry
    /// a forget or prune removed. Two processes sharing a store must not run a re-embed and a removal at once — a
    /// repeat forget of the scope clears any vector left behind.</para></summary>
    /// <param name="taskKey">The task to re-embed.</param>
    /// <param name="scope">The scope, or null for every scope of the task.</param>
    /// <param name="ct">Cancellation, which is never swallowed; entries already re-embedded stay so.</param>
    /// <returns>How many entries were re-embedded, and how many could not be because their embed call failed.</returns>
    /// <exception cref="InvalidOperationException">The engine has no vector index, or nothing can embed.</exception>
    Task<MemoryReindexResult> ReindexAsync(string taskKey, string? scope = null, CancellationToken ct = default);
}

/// <summary>What a re-embed did: <paramref name="Indexed"/> entries re-embedded, <paramref name="Failed"/> left on
/// their old vector because their embed call failed. An entry removed while the pass ran counts in neither.</summary>
public sealed record MemoryReindexResult(int Indexed, int Failed);
