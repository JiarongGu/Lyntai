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
    /// be scored against the wrong one; under a declared vector space it finds no neighbours until the pass ends.</para>
    /// <para>Under a declared vector space an interrupted pass RESUMES: a rerun into the same space skips what the
    /// earlier one finished. A rerun into another space, or with none declared, starts over.</para>
    /// <para>A removal landing mid-pass must not be undone by it: the pass never writes a vector back for an entry a
    /// forget or prune removed. The graph engine holds this for a forget from any instance over the same store,
    /// another process included, by re-reading each entry after its upsert; a prune from another instance is
    /// best-effort and may leave an orphan, which a forget of the scope clears.</para></summary>
    /// <param name="taskKey">The task to re-embed.</param>
    /// <param name="scope">The scope, or null for every scope of the task.</param>
    /// <param name="ct">Cancellation, which is never swallowed; entries already re-embedded stay so.</param>
    /// <returns>How many entries were re-embedded, and how many could not be because their embed call failed.</returns>
    /// <exception cref="InvalidOperationException">The engine has no vector index, or nothing can embed.</exception>
    Task<MemoryReindexResult> ReindexAsync(string taskKey, string? scope = null, CancellationToken ct = default);
}

/// <summary>What a re-embed did: <paramref name="Indexed"/> entries re-embedded, <paramref name="Failed"/> left on
/// their old vector because their embed call failed. An entry removed while the pass ran counts in neither.</summary>
public sealed record MemoryReindexResult(int Indexed, int Failed)
{
    /// <summary>The members of a blend that cannot re-embed and were passed over, by name — empty from a single
    /// engine. A member here that holds vectors of its own (a semantic member does) is still on the old model.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>Entries this pass left alone because an earlier, interrupted pass into the same declared vector space
    /// had already re-embedded them — zero unless a space is declared.</summary>
    public int Resumed { get; init; }
}

/// <summary>OPTIONAL capability: an engine that can carry one task's memory out as portable JSONL and take such a file
/// in, for moving a memory between stores, backends or deployments. The format is <c>docs/memory.md</c> §9's "Carry a
/// memory to another store".
///
/// <para><b>Scoped, with no "everything" form.</b> Both verbs name a task; neither has a default for it or for the
/// scope, so a whole-store dump is never one forgotten argument away.</para>
///
/// <para><b>No vectors travel.</b> A vector is a projection of the content under one embedding model; the engine
/// receiving the file indexes on write, as for any other write.</para>
///
/// <para><b>What an import promises, and what it cannot.</b> It writes only under the task and scope it names; it never
/// overwrites or duplicates an entry the target already holds with the same identity, so importing a file twice adds
/// nothing the second time; and an entry exported disabled arrives disabled. It cannot know what the target REMOVED:
/// no store keeps a record of a forget, so importing a file taken before a forget brings that content back. An import
/// is the caller's explicit act — import only a file taken after the last removal you mean to keep.</para>
///
/// <para><b>A graph engine carries decay state and links</b>: each entry's stability, difficulty, recall count, signals,
/// provenance and grade, its ages relative to where its engine stands, its subjects and the links between entries,
/// through its store's <see cref="IPortableMemoryGraphStore"/> — without one it refuses both verbs.</para>
///
/// <para>Reached by a type test, like <see cref="IPrunableMemory"/>; the blend around an engine declares it too, fanning
/// out to the members that have it.</para></summary>
public interface IPortableMemory
{
    /// <summary>Write every entry under (<paramref name="taskKey"/>, <paramref name="scope"/>) to
    /// <paramref name="destination"/>, one JSON object per line, disabled entries included.</summary>
    /// <param name="taskKey">The task to export; required.</param>
    /// <param name="scope">The scope, or null for every scope of the task — stated, never defaulted.</param>
    /// <param name="destination">Receives the lines; it is neither flushed nor closed.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    /// <exception cref="ArgumentException"><paramref name="taskKey"/> is blank.</exception>
    Task<MemoryExportResult> ExportAsync(string taskKey, string? scope, TextWriter destination,
        CancellationToken ct = default);

    /// <summary>Read a file <see cref="ExportAsync"/> wrote and write its entries under (<paramref name="taskKey"/>,
    /// <paramref name="scope"/>), whatever task they were exported from.
    /// <para>The file is read and checked whole before the first write: a malformed line, a version this reader does
    /// not know, or a record this engine cannot hold throws <see cref="InvalidDataException"/> naming the line, and
    /// nothing is written.</para></summary>
    /// <param name="taskKey">The task every entry is written under; required.</param>
    /// <param name="scope">The scope every entry is written under, or null to keep each line's own scope inside
    /// <paramref name="taskKey"/>.</param>
    /// <param name="source">The lines; read to the end, not closed.</param>
    /// <param name="ct">Cancellation, which is never swallowed; entries already written stay.</param>
    /// <exception cref="ArgumentException"><paramref name="taskKey"/> is blank.</exception>
    /// <exception cref="InvalidDataException">A line cannot be honoured; nothing was written.</exception>
    Task<MemoryImportResult> ImportAsync(string taskKey, string? scope, TextReader source,
        CancellationToken ct = default);
}

/// <summary>What an export wrote: <paramref name="Exported"/> entries, one line each.</summary>
public sealed record MemoryExportResult(int Exported)
{
    /// <summary>The members of a blend that cannot export and were passed over, by name — empty from a single engine.
    /// Anything they hold is NOT in the file.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    /// <summary>Links written beside the entries, one line each — zero from an engine whose entries are not
    /// connected.</summary>
    public int Links { get; init; }
}

/// <summary>What an import did: <paramref name="Imported"/> entries written, <paramref name="AlreadyPresent"/> lines
/// left alone because the target already held that entry or an earlier line carried it.</summary>
public sealed record MemoryImportResult(int Imported, int AlreadyPresent)
{
    /// <summary>Links written. A link the target already held is left alone and counted in neither this nor
    /// <see cref="LinksDropped"/>.</summary>
    public int Links { get; init; }

    /// <summary>Links not written because an endpoint is not an entry of the file — one in a scope or task the export
    /// did not cover.</summary>
    public int LinksDropped { get; init; }
}
