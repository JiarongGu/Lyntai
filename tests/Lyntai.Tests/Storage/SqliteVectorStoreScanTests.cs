using Dapper;
using Lyntai.Memory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>What the streaming scan in <see cref="SqliteVectorStore"/> must keep from the one it replaced: the
/// same score to the bit, and a row it cannot read ranked last rather than thrown on.</summary>
public sealed class SqliteVectorStoreScanTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task A_search_scores_exactly_as_VectorMath_Cosine_does()
    {
        var store = new SqliteVectorStore(_db.Factory);
        var rng = new Random(7);
        var vectors = new Dictionary<string, float[]>();
        for (var i = 0; i < 60; i++)
        {
            // magnitudes from 1e-7 to 1e4 so the stored text carries exponents and long mantissas
            var scale = (float)Math.Pow(10, rng.Next(-7, 5));
            var v = Enumerable.Range(0, 24).Select(_ => (float)(rng.NextDouble() - 0.5) * scale).ToArray();
            vectors[$"v{i:D2}"] = v;
            await store.UpsertAsync("scan", $"v{i:D2}", v, $"payload {i}");
        }

        var query = Enumerable.Range(0, 24).Select(_ => (float)(rng.NextDouble() - 0.5)).ToArray();
        var hits = await store.SearchAsync("scan", query, 60);

        Assert.Equal(60, hits.Count);
        foreach (var hit in hits)
        {
            Assert.Equal(VectorMath.Cosine(query, vectors[hit.Id]), hit.Score);
            Assert.Equal($"payload {int.Parse(hit.Id[1..])}", hit.Payload);
        }

        Assert.Equal(hits.OrderByDescending(h => h.Score).ThenBy(h => h.Id, StringComparer.Ordinal).Select(h => h.Id),
            hits.Select(h => h.Id));
    }

    [Fact]
    public async Task Only_the_best_k_are_kept_and_they_are_the_same_k_a_full_sort_keeps()
    {
        var store = new SqliteVectorStore(_db.Factory);
        var rng = new Random(11);
        var vectors = new Dictionary<string, float[]>();
        for (var i = 0; i < 80; i++)
        {
            // a coarse grid makes equal scores common, so the k boundary falls inside a tie
            var v = new float[] { rng.Next(0, 3), rng.Next(0, 3), 1 };
            vectors[$"t{i:D2}"] = v;
            await store.UpsertAsync("ties", $"t{i:D2}", v, "p");
        }

        float[] query = [1, 2, 1];
        var expected = vectors
            .Select(kv => (Id: kv.Key, Score: VectorMath.Cosine(query, kv.Value)))
            .OrderByDescending(x => x.Score).ThenBy(x => x.Id, StringComparer.Ordinal).Take(7).Select(x => x.Id);

        var hits = await store.SearchAsync("ties", query, 7);

        Assert.Equal(expected, hits.Select(h => h.Id));
    }

    [Fact]
    public async Task A_row_whose_text_is_not_a_vector_ranks_last_instead_of_failing_the_search()
    {
        var store = new SqliteVectorStore(_db.Factory);
        await store.UpsertAsync("bad", "good", [1, 0, 0], "g");
        await using (var conn = await _db.Factory.OpenAsync())
            await conn.ExecuteAsync("INSERT INTO lyntai_vector (collection, vec_id, vector, payload) VALUES ('bad', 'broken', 'not json', 'b')");

        var hits = await store.SearchAsync("bad", [1, 0, 0], 5);

        Assert.Equal(["good", "broken"], hits.Select(h => h.Id));
        Assert.Equal(0, hits[1].Score);
    }
}
