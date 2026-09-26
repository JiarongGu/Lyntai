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
