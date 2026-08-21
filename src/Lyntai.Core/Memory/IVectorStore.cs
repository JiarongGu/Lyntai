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
    /// <paramref name="query"/> by cosine similarity, highest score first. Order among EQUAL scores is
    /// UNSPECIFIED BY THIS CONTRACT — it may differ between backends, so a caller needing a top-k that is
    /// stable ACROSS backends must break the tie itself. Per backend: <see cref="InMemoryVectorStore"/> does
    /// break ties (by id, ordinal ascending) because it ranks out of a hash table whose enumeration order
    /// varies between runs; the SQL-backed stores do not, so their ties fall back to the order the rows
    /// arrive in.</summary>
    Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default);

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

/// <summary>A search result: the stored <paramref name="Payload"/> and its cosine <paramref name="Score"/>
/// (in [-1, 1]; higher is more similar).</summary>
public sealed record VectorMatch(string Id, string Payload, double Score);
