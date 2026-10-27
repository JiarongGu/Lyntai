using System.Collections.Concurrent;

namespace Lyntai.Memory;

/// <summary>
/// A process-local <see cref="IVectorStore"/> that keeps vectors in memory and searches them brute-force
/// (exact cosine over every entry in the collection). The zero-dependency default for semantic memory —
/// fine up to some thousands of entries per collection; for larger corpora or persistence across restarts,
/// register a real vector backend (pgvector, sqlite-vec, …) instead. Thread-safe, and its top-k is
/// DETERMINISTIC: equal scores are broken by id (see <see cref="SearchAsync(string, float[], int, CancellationToken)"/>).
/// </summary>
public sealed class InMemoryVectorStore : IListableVectorStore, IReadableVectorStore
{
    /// <summary>The vector, its payload and its squared length — summed once at upsert, in the order
    /// <see cref="VectorMath.Cosine(float[], float[])"/> sums it, so a search reuses it and still scores
    /// identically.</summary>
    private readonly record struct Item(float[] Vector, string Payload, double SquaredNorm);

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, Item>> _collections = new();

    public Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
    {
        var col = _collections.GetOrAdd(collection, _ => new ConcurrentDictionary<string, Item>());
        // a copy, so a caller reusing its buffer can neither change what is stored nor desync the cached norm
        var stored = (float[])vector.Clone();
        col[id] = new Item(stored, payload, SquaredNorm(stored));
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
    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
        Search(collection, query, k, filter: null);

    /// <inheritdoc />
    /// <remarks>Filters BEFORE ranking, so an excluded entry costs no cosine.</remarks>
    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, VectorSearchFilter filter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return Search(collection, query, k, filter);
    }

    private Task<IReadOnlyList<VectorMatch>> Search(string collection, float[] query, int k, VectorSearchFilter? filter)
    {
        if (k <= 0 || !_collections.TryGetValue(collection, out var col) || col.IsEmpty)
            return Task.FromResult<IReadOnlyList<VectorMatch>>([]);

        var admits = filter?.Matcher();
        var queryNorm = SquaredNorm(query);
        // A bounded heap whose root is the worst kept entry — lowest score, then greatest id, the reverse of the
        // result order — so a search is one pass and k allocations, not a sort of the whole collection. The id
        // tiebreak is load-bearing for the k boundary (VectorStoreContract.The_k_boundary_keeps_the_same_tied_entries).
        var best = new PriorityQueue<VectorMatch, (double Score, string Id)>(WorstFirst.Instance);
        foreach (var (id, item) in col)
        {
            if (admits is not null && !admits(id)) continue;
            var score = Cosine(query, queryNorm, item);
            if (best.Count < k) best.Enqueue(new VectorMatch(id, item.Payload, score), (score, id));
            else if (best.TryPeek(out _, out var worst) && WorstFirst.Instance.Compare((score, id), worst) > 0)
                best.EnqueueDequeue(new VectorMatch(id, item.Payload, score), (score, id));
        }

        var ranked = new List<VectorMatch>(best.Count);
        while (best.TryDequeue(out var match, out _)) ranked.Add(match);
        ranked.Reverse();
        return Task.FromResult<IReadOnlyList<VectorMatch>>(ranked);
    }

    private static double SquaredNorm(float[] vector)
    {
        double sum = 0;
        foreach (var v in vector) sum += (double)v * v;
        return sum;
    }

    // VectorMath.Cosine with the stored vector's norm already known; every sum runs in the same order, so the
    // result is the same double.
    private static double Cosine(float[] query, double queryNorm, Item item)
    {
        var vector = item.Vector;
        if (query.Length != vector.Length || query.Length == 0) return 0;
        double dot = 0;
        for (var i = 0; i < query.Length; i++) dot += (double)query[i] * vector[i];
        return queryNorm == 0 || item.SquaredNorm == 0 ? 0 : dot / (Math.Sqrt(queryNorm) * Math.Sqrt(item.SquaredNorm));
    }

    private sealed class WorstFirst : IComparer<(double Score, string Id)>
    {
        public static readonly WorstFirst Instance = new();

        public int Compare((double Score, string Id) x, (double Score, string Id) y) =>
            x.Score != y.Score ? x.Score.CompareTo(y.Score) : string.CompareOrdinal(y.Id, x.Id);
    }

    public Task<IReadOnlyList<VectorEntry>> GetAsync(string collection, IReadOnlyCollection<string> ids,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (!_collections.TryGetValue(collection, out var col)) return Task.FromResult<IReadOnlyList<VectorEntry>>([]);
        IReadOnlyList<VectorEntry> read = [.. ids.Distinct(StringComparer.Ordinal)
            .Select(id => col.TryGetValue(id, out var item) ? new VectorEntry(id, [.. item.Vector], item.Payload) : null)
            .OfType<VectorEntry>()
            .OrderBy(e => e.Id, StringComparer.Ordinal)];
        return Task.FromResult(read);
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

    /// <inheritdoc />
    /// <remarks>An EMPTY collection is not listed: the dictionary keeps a bucket after its last entry is
    /// deleted, and reporting it would tell a caller a scope exists that holds nothing — which the persistent
    /// backends, where a row-less collection has no rows at all, could not agree with.</remarks>
    public Task<IReadOnlyList<string>> ListCollectionsAsync(string prefix, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return Task.FromResult<IReadOnlyList<string>>(
            [.. _collections.Where(kv => !kv.Value.IsEmpty && kv.Key.StartsWith(prefix, StringComparison.Ordinal))
                .Select(kv => kv.Key)]);
    }
}
