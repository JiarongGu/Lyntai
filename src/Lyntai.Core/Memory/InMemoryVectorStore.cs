using System.Collections.Concurrent;

namespace Lyntai.Memory;

/// <summary>
/// A process-local <see cref="IVectorStore"/> that keeps vectors in memory and searches them brute-force
/// (exact cosine over every entry in the collection). The zero-dependency default for semantic memory —
/// fine up to some thousands of entries per collection; for larger corpora or persistence across restarts,
/// register a real vector backend (pgvector, sqlite-vec, …) instead. Thread-safe.
/// </summary>
public sealed class InMemoryVectorStore : IVectorStore
{
    private readonly record struct Item(float[] Vector, string Payload);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Item>> _collections = new();

    public Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
    {
        var col = _collections.GetOrAdd(collection, _ => new ConcurrentDictionary<string, Item>());
        col[id] = new Item(vector, payload);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default)
    {
        if (k <= 0 || !_collections.TryGetValue(collection, out var col) || col.IsEmpty)
            return Task.FromResult<IReadOnlyList<VectorMatch>>([]);

        var ranked = col
            .Select(kv => new VectorMatch(kv.Key, kv.Value.Payload, VectorMath.Cosine(query, kv.Value.Vector)))
            .OrderByDescending(m => m.Score)
            .Take(k)
            .ToList();
        return Task.FromResult<IReadOnlyList<VectorMatch>>(ranked);
    }

    public Task RemoveCollectionAsync(string collection, CancellationToken ct = default)
    {
        _collections.TryRemove(collection, out _);
        return Task.CompletedTask;
    }
}
