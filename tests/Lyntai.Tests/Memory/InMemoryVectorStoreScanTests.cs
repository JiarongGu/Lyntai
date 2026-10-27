using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>What the one-pass top-k in <see cref="InMemoryVectorStore"/> must keep from the sort-everything search it
/// replaced: the same scores to the bit, the same members at a tie on the k boundary, and a stored vector that a
/// caller's later writes to its own buffer cannot move.</summary>
public sealed class InMemoryVectorStoreScanTests
{
    [Fact]
    public async Task A_search_returns_what_sorting_every_cosine_returns()
    {
        var store = new InMemoryVectorStore();
        var rng = new Random(3);
        var vectors = new Dictionary<string, float[]>();
        for (var i = 0; i < 500; i++)
        {
            // a coarse grid on a few axes makes equal scores common, so k falls inside ties
            var v = new float[] { rng.Next(0, 4), rng.Next(0, 4), rng.Next(0, 4), 1 };
            vectors[$"m{i:D3}"] = v;
            await store.UpsertAsync("c", $"m{i:D3}", v, $"p{i}");
        }

        float[] query = [1, 2, 3, 1];
        foreach (var k in new[] { 1, 7, 40, 500, 900 })
        {
            var expected = vectors
                .Select(kv => (Id: kv.Key, Score: VectorMath.Cosine(query, kv.Value)))
                .OrderByDescending(x => x.Score).ThenBy(x => x.Id, StringComparer.Ordinal).Take(k).ToList();

            var hits = await store.SearchAsync("c", query, k);

            Assert.Equal(expected.Select(x => x.Id), hits.Select(h => h.Id));
            Assert.Equal(expected.Select(x => x.Score), hits.Select(h => h.Score));
        }
    }

    [Fact]
    public async Task A_caller_reusing_its_buffer_does_not_change_what_was_stored()
    {
        var store = new InMemoryVectorStore();
        var buffer = new float[] { 1, 0 };
        await store.UpsertAsync("c", "a", buffer, "p");
        buffer[0] = 0;
        buffer[1] = 1;

        var hits = await store.SearchAsync("c", [1, 0], 1);

        Assert.Equal(1.0, hits[0].Score, 6);
    }
}
