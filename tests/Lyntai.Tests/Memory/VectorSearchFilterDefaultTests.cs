using System.Collections.Concurrent;
using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>The filtered <see cref="IVectorStore.SearchAsync(string, float[], int, VectorSearchFilter, CancellationToken)"/>
/// DEFAULT body — what a store an application wrote gets without a change — held to the same contract facts the
/// shipped stores' overrides are.</summary>
public class VectorSearchFilterDefaultTests
{
    private static IVectorStore New() => new MinimalStore();

    [Fact] public Task Admits() => VectorStoreContract.A_filter_admits_only_its_ids(New(), "d1");
    [Fact] public Task Exclusion_wins() => VectorStoreContract.An_exclusion_wins_over_an_inclusion(New(), "d2");
    [Fact] public Task Empty() => VectorStoreContract.An_empty_inclusion_returns_nothing(New(), "d3");
    [Fact] public Task Tie() => VectorStoreContract.The_tiebreak_holds_within_the_admitted_set(New(), "d4");
    [Fact] public Task Many_ids() => VectorStoreContract.A_large_filter_searches_without_failing(New(), "d5");

    [Fact]
    public async Task A_null_filter_is_refused()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => New().SearchAsync("d6", [1f], 1, null!));
    }

    [Fact]
    public async Task A_filter_reads_its_id_sets_once_per_search_not_once_per_candidate()
    {
        // the default body and the in-process store both test every candidate; a per-candidate scan of the id list
        // makes a 5,000-id filter over 5,000 entries 25M comparisons
        foreach (var store in new IVectorStore[] { New(), new InMemoryVectorStore() })
        {
            for (var i = 0; i < 50; i++) await store.UpsertAsync("e", $"id-{i}", [1f, i], $"{i}");
            var (ids, exclude) = (new CountingIds(["id-3", "id-7"]), new CountingIds(["id-7"]));

            var hits = await store.SearchAsync("e", [1f, 0f], 10, new VectorSearchFilter { Ids = ids, ExcludeIds = exclude });

            Assert.Equal(["id-3"], hits.Select(h => h.Id));
            Assert.True(ids.Reads <= 1 && exclude.Reads <= 1,
                $"{store.GetType().Name} read Ids {ids.Reads}x and ExcludeIds {exclude.Reads}x for one search");
        }
    }

    /// <summary>An id set that counts how often it is enumerated — and is not an <see cref="ICollection{T}"/>, so
    /// nothing can ask it <c>Contains</c> without enumerating it.</summary>
    private sealed class CountingIds(IReadOnlyList<string> ids) : IReadOnlyCollection<string>
    {
        public int Reads { get; private set; }

        public int Count => ids.Count;

        public IEnumerator<string> GetEnumerator()
        {
            Reads++;
            return ids.GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A store implementing ONLY the four required members, with the contract's ordering.</summary>
    private sealed class MinimalStore : IVectorStore
    {
        private readonly ConcurrentDictionary<(string, string), float[]> _vectors = new();

        public Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
        {
            _vectors[(collection, id)] = vector;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<VectorMatch>>([.. _vectors
                .Where(kv => kv.Key.Item1 == collection)
                .Select(kv => new VectorMatch(kv.Key.Item2, kv.Key.Item2, VectorMath.Cosine(query, kv.Value)))
                .OrderByDescending(m => m.Score)
                .ThenBy(m => m.Id, StringComparer.Ordinal)
                .Take(k)]);

        public Task DeleteAsync(string collection, string id, CancellationToken ct = default)
        {
            _vectors.TryRemove((collection, id), out _);
            return Task.CompletedTask;
        }

        public Task RemoveCollectionAsync(string collection, CancellationToken ct = default)
        {
            foreach (var key in _vectors.Keys.Where(k => k.Item1 == collection)) _vectors.TryRemove(key, out _);
            return Task.CompletedTask;
        }
    }
}
