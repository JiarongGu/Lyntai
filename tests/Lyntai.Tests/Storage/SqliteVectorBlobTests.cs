using Dapper;
using Lyntai.Memory;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Tests.Storage;

/// <summary>The binary vector column: a row written before it (text only) reads and scores exactly as one written
/// after, and the migration's backfill produces the bytes a write would.</summary>
public sealed class SqliteVectorBlobTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    private static float[] Random(Random rng, int dim, double scale) =>
        [.. Enumerable.Range(0, dim).Select(_ => (float)((rng.NextDouble() - 0.5) * scale))];

    private async Task InsertTextOnlyAsync(string collection, string id, string json)
    {
        await using var conn = await _db.Factory.OpenAsync();
        await conn.ExecuteAsync("INSERT INTO lyntai_vector (collection, vec_id, vector, payload) VALUES (@collection, @id, @json, 'p')",
            new { collection, id, json });
    }

    [Fact]
    public async Task A_row_written_before_the_column_scores_and_reads_back_as_one_written_after()
    {
        var store = new SqliteVectorStore(_db.Factory);
        var rng = new Random(3);
        var query = Random(rng, 32, 1);
        var written = new Dictionary<string, float[]>();
        for (var i = 0; i < 40; i++)
        {
            var v = Random(rng, 32, Math.Pow(10, rng.Next(-7, 5)));
            written[$"new{i:D2}"] = v;
            await store.UpsertAsync("mixed", $"new{i:D2}", v, "p");
            // the same vector, as the old write stored it: JSON text and no blob
            await InsertTextOnlyAsync("mixed", $"old{i:D2}", System.Text.Json.JsonSerializer.Serialize(v));
        }

        var hits = await store.SearchAsync("mixed", query, 80);

        Assert.Equal(80, hits.Count);
        foreach (var hit in hits)
            Assert.Equal(VectorMath.Cosine(query, written[$"new{hit.Id[3..]}"]), hit.Score);
        foreach (var pair in hits.Where(h => h.Id.StartsWith("old")).Zip(
            hits.Where(h => h.Id.StartsWith("new")), (o, n) => (o, n)))
            Assert.Equal(pair.n.Score, pair.o.Score);

        var read = (await store.GetAsync("mixed", ["old07", "new07"])).ToDictionary(e => e.Id, e => e.Vector);
        Assert.Equal(written["new07"], read["old07"]);
        Assert.Equal(written["new07"], read["new07"]);
    }

    [Fact]
    public async Task A_write_stores_the_blob_and_keeps_the_text()
    {
        var store = new SqliteVectorStore(_db.Factory);
        float[] v = [1.5f, -2.25f, 0, 3e-8f];
        await store.UpsertAsync("c", "a", v, "p");

        await using var conn = await _db.Factory.OpenAsync();
        var blob = await conn.ExecuteScalarAsync<byte[]>("SELECT vector_blob FROM lyntai_vector WHERE vec_id = 'a'");
        var text = await conn.ExecuteScalarAsync<string>("SELECT vector FROM lyntai_vector WHERE vec_id = 'a'");

        Assert.Equal(v.Length * 4, blob!.Length);
        Assert.Equal(v, Enumerable.Range(0, v.Length).Select(i => BitConverter.ToSingle(blob, i * 4)));
        Assert.Equal(v, System.Text.Json.JsonSerializer.Deserialize<float[]>(text!));
    }

    [Fact]
    public async Task A_rewrite_replaces_the_blob_so_the_text_and_the_blob_never_disagree()
    {
        var store = new SqliteVectorStore(_db.Factory);
        await store.UpsertAsync("c", "a", [1, 0, 0], "p");
        await store.UpsertAsync("c", "a", [0, 1, 0], "p");

        var hits = await store.SearchAsync("c", [0, 1, 0], 1);

        Assert.Equal(1.0, hits[0].Score);
    }

    [Fact]
    public async Task A_blob_of_a_length_that_is_not_whole_floats_ranks_last_instead_of_failing_the_search()
    {
        var store = new SqliteVectorStore(_db.Factory);
        await store.UpsertAsync("c", "good", [1, 0, 0], "g");
        await using (var conn = await _db.Factory.OpenAsync())
            await conn.ExecuteAsync("INSERT INTO lyntai_vector (collection, vec_id, vector, payload, vector_blob) VALUES ('c', 'torn', '[1,0,0]', 'b', x'0102030405')");

        var hits = await store.SearchAsync("c", [1, 0, 0], 5);

        Assert.Equal(["good", "torn"], hits.Select(h => h.Id));
        Assert.Equal(0, hits[1].Score);
    }

    [Fact]
    public async Task The_backfill_fills_every_parseable_row_across_pages_and_leaves_the_rest_unset()
    {
        var rng = new Random(5);
        var expected = new Dictionary<(string, string), float[]>();
        for (var i = 0; i < 1300; i++)
        {
            var v = Random(rng, 8, Math.Pow(10, rng.Next(-7, 5)));
            var key = ($"col{i % 3}", $"id{i:D4}");
            expected[key] = v;
            await InsertTextOnlyAsync(key.Item1, key.Item2, System.Text.Json.JsonSerializer.Serialize(v));
        }

        await InsertTextOnlyAsync("col0", "broken", "not json");

        // put the database back where it stood before this migration: no column, and no record that it ran
        await using (var conn = await _db.Factory.OpenAsync())
        {
            await conn.ExecuteAsync("UPDATE lyntai_vector SET vector_blob = NULL");
            await conn.ExecuteAsync("ALTER TABLE lyntai_vector DROP COLUMN vector_blob");
            await conn.ExecuteAsync("DELETE FROM lyntai_version_info WHERE Version = 202610281422");
        }

        MigrationRunnerService.MigrateUp(_db.Path);

        await using var check = await _db.Factory.OpenAsync();
        var rows = (await check.QueryAsync<(string Collection, string Id, byte[]? Blob)>(
            "SELECT collection AS Collection, vec_id AS Id, vector_blob AS Blob FROM lyntai_vector")).ToList();
        Assert.Equal(1301, rows.Count);
        Assert.Null(rows.Single(r => r.Id == "broken").Blob);
        foreach (var row in rows.Where(r => r.Id != "broken"))
        {
            var v = expected[(row.Collection, row.Id)];
            Assert.Equal(v.Select(BitConverter.SingleToInt32Bits),
                Enumerable.Range(0, v.Length).Select(i => BitConverter.ToInt32(row.Blob!, i * 4)));
        }
    }
}
