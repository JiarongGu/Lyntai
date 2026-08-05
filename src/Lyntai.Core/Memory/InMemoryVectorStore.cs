using System.Collections.Concurrent;

namespace Lyntai.Memory;

/// <summary>
/// A process-local <see cref="IVectorStore"/> that keeps vectors in memory and searches them brute-force
/// (exact cosine over every entry in the collection). The zero-dependency default for semantic memory —
/// fine up to some thousands of entries per collection; for larger corpora or persistence across restarts,
/// register a real vector backend (pgvector, sqlite-vec, …) instead. Thread-safe, and its top-k is
/// DETERMINISTIC: equal scores are broken by id (see <see cref="SearchAsync"/>).
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

    /// <summary>The <paramref name="k"/> nearest entries by cosine similarity, highest score first, with
    /// EQUAL scores broken by id (ordinal, ascending). The tiebreak is load-bearing, not tidiness: the source
    /// is a hash table whose enumeration order depends on bucket layout and resizing, and LINQ's
    /// <c>OrderByDescending</c> is a STABLE sort — so it faithfully preserves an order that varies between
    /// runs, which is exactly the wrong thing to preserve. Without the tiebreak, two entries with the same
    /// score swap places between processes and, at the <paramref name="k"/> boundary, one of them silently
    /// drops out of the result altogether (the same defect <c>storage.md</c> records for an
    /// <c>ORDER BY</c> on a non-unique column). Ids are unique within a collection, so this is a total
    /// order.</summary>
    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default)
    {
        if (k <= 0 || !_collections.TryGetValue(collection, out var col) || col.IsEmpty)
            return Task.FromResult<IReadOnlyList<VectorMatch>>([]);

        var ranked = col
            .Select(kv => new VectorMatch(kv.Key, kv.Value.Payload, VectorMath.Cosine(query, kv.Value.Vector)))
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Take(k)
            .ToList();
        return Task.FromResult<IReadOnlyList<VectorMatch>>(ranked);
    }

    public Task DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        if (_collections.TryGetValue(collection, out var col))
            col.TryRemove(id, out _); // no-op if the id (or collection) is absent
        return Task.CompletedTask;
    }

    public Task RemoveCollectionAsync(string collection, CancellationToken ct = default)
    {
        _collections.TryRemove(collection, out _);
        return Task.CompletedTask;
    }
}
