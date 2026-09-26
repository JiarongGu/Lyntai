namespace Lyntai.Memory;

/// <summary>
/// The vector-persistence seam behind <see cref="ISemanticMemory"/>: stores embedding vectors + their
/// text payload in named collections and does nearest-neighbour (cosine) search. The built-in
/// <see cref="InMemoryVectorStore"/> (brute-force) is the default; register your own <see cref="IVectorStore"/>
/// before wiring embeddings to back it with pgvector / sqlite-vec / a dedicated vector DB — the recall
/// logic doesn't change. Collections scope vectors (semantic memory uses one per task+scope).
/// </summary>
public interface IVectorStore
{
    /// <summary>Insert or replace the vector + payload stored under <paramref name="id"/> in
    /// <paramref name="collection"/> (re-upserting the same id overwrites — the dedup mechanism).</summary>
    Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default);

    /// <summary>The <paramref name="k"/> nearest entries in <paramref name="collection"/> to
    /// <paramref name="query"/> by cosine similarity, highest score first, EQUAL scores ordered by id
    /// (ordinal ascending) — so the same search returns the same top-k on every run and every backend, and a
    /// tie at the k boundary drops the same entry each time. An implementation must honour the tiebreak: a
    /// hash-table or row-arrival order varies between runs.</summary>
    Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default);

    /// <summary>The <paramref name="k"/> nearest entries <paramref name="filter"/> admits, ranked and tie-broken as
    /// the unfiltered search is — the filter applies BEFORE the <paramref name="k"/> boundary, so a narrowed search
    /// still returns up to <paramref name="k"/> results.
    /// <para><b>The default body is correct and only slower</b>: it ranks the whole collection, keeps what the
    /// filter admits and takes <paramref name="k"/>, so a store of your own serves this unchanged. The shipped stores
    /// override it to filter inside their query.</para></summary>
    /// <param name="collection">The collection to search.</param>
    /// <param name="query">The query vector.</param>
    /// <param name="k">How many results at most; zero or less returns nothing.</param>
    /// <param name="filter">Which ids may come back.</param>
    /// <param name="ct">Cancellation.</param>
    async Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, VectorSearchFilter filter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        if (k <= 0) return [];
        var ranked = await SearchAsync(collection, query, int.MaxValue, ct).ConfigureAwait(false);
        return [.. ranked.Where(m => filter.Admits(m.Id)).Take(k)];
    }

    /// <summary>Remove the single vector stored under <paramref name="id"/> in <paramref name="collection"/>.
    /// No-op if absent. (Whole-collection drop is <see cref="RemoveCollectionAsync"/>.)</summary>
    Task DeleteAsync(string collection, string id, CancellationToken ct = default);

    /// <summary>Drop an entire collection (backs <see cref="ISemanticMemory.ForgetAsync"/>).</summary>
    Task RemoveCollectionAsync(string collection, CancellationToken ct = default);
}

/// <summary>
/// The OPTIONAL half of <see cref="IVectorStore"/>: enumerating the collections a store holds.
///
/// <para><b>What it unlocks.</b> Semantic memory addresses one collection per (task, scope), so
/// <c>ISemanticMemory.RecallAsync</c> with a null scope — "search every scope of this task" — has nothing to
/// search without a way to ask which scopes exist. All three shipped stores implement this; a BYO store that
/// does not simply leaves the cross-scope recall yielding nothing, which is what it did before this
/// interface existed.</para>
///
/// <para><b>Separate rather than a member of <see cref="IVectorStore"/></b>: adding a required member to an
/// interface consumers implement is a major bump, and a default body would have made a store that cannot
/// enumerate indistinguishable from one that holds nothing — the silent shape this seam exists to end. It is
/// the same optional-capability pattern <c>IExpandableMemory</c> and <c>IPrunableMemory</c> use.</para>
/// </summary>
public interface IListableVectorStore : IVectorStore
{
    /// <summary>Every collection name beginning with <paramref name="prefix"/>, compared ORDINALLY (byte
    /// order, case-sensitive) — an empty prefix lists them all. Order is UNSPECIFIED; a caller needing a
    /// stable order sorts. Returns an empty list rather than throwing when nothing matches.</summary>
    /// <param name="prefix">The literal prefix to match. It is data, never a pattern: no wildcard, escape or
    /// case-folding rule applies to it.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<string>> ListCollectionsAsync(string prefix, CancellationToken ct = default);
}

/// <summary>
/// The OPTIONAL half of <see cref="IVectorStore"/> that reads stored entries back by id — so an application that
/// keeps its own corpus reads the vectors it stored instead of embedding everything again.
/// <para><b>Separate rather than a member of <see cref="IVectorStore"/></b>, as <see cref="IListableVectorStore"/>
/// is: a required member is a major bump for every store an application wrote, and a default returning nothing
/// would make a store that cannot read indistinguishable from one holding nothing. All three shipped stores
/// implement it.</para>
/// </summary>
public interface IReadableVectorStore : IVectorStore
{
    /// <summary>The entries stored under <paramref name="ids"/> in <paramref name="collection"/>, ordered by id
    /// (ordinal). An absent id is left out rather than failing, an id asked for twice comes back once, and an empty
    /// <paramref name="ids"/> reads nothing. Each vector is exactly the one upserted, bit for bit, and a fresh
    /// array: changing it changes nothing stored.</summary>
    /// <param name="collection">The collection to read.</param>
    /// <param name="ids">The ids to read; any number of them.</param>
    /// <param name="ct">Cancellation.</param>
    Task<IReadOnlyList<VectorEntry>> GetAsync(string collection, IReadOnlyCollection<string> ids,
        CancellationToken ct = default);
}

/// <summary>A search result: the stored <paramref name="Payload"/> and its cosine <paramref name="Score"/>
/// (in [-1, 1]; higher is more similar).</summary>
public sealed record VectorMatch(string Id, string Payload, double Score);

/// <summary>Narrows a nearest-neighbour search to ids the caller keeps elsewhere: an attribute it filters on lives
/// in its own data, and it passes the ids that match.</summary>
public sealed record VectorSearchFilter
{
    /// <summary>Only these ids may come back. Null places no restriction; an EMPTY set admits nothing.</summary>
    public IReadOnlyCollection<string>? Ids { get; init; }

    /// <summary>These ids never come back, whether or not <see cref="Ids"/> names them.</summary>
    public IReadOnlyCollection<string>? ExcludeIds { get; init; }

    /// <summary>Whether <paramref name="id"/> may come back (ids compare ordinally) — what a store that filters in
    /// process asks of each candidate.</summary>
    /// <param name="id">A stored entry's id.</param>
    public bool Admits(string id) =>
        (Ids is null || Ids.Contains(id, StringComparer.Ordinal))
        && (ExcludeIds is null || !ExcludeIds.Contains(id, StringComparer.Ordinal));
}

/// <summary>A stored entry read back by <see cref="IReadableVectorStore.GetAsync"/>: its id, the
/// <paramref name="Vector"/> exactly as upserted, and its <paramref name="Payload"/>.</summary>
public sealed record VectorEntry(string Id, float[] Vector, string Payload);
