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
    /// <paramref name="query"/> by cosine similarity, highest score first.</summary>
    Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default);

    /// <summary>Drop an entire collection (backs <see cref="ISemanticMemory.ForgetAsync"/>).</summary>
    Task RemoveCollectionAsync(string collection, CancellationToken ct = default);
}

/// <summary>A search result: the stored <paramref name="Payload"/> and its cosine <paramref name="Score"/>
/// (in [-1, 1]; higher is more similar).</summary>
public sealed record VectorMatch(string Id, string Payload, double Score);
