using System.Text.Json;
using Dapper;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Sqlite;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>
/// A malformed curated or node metadata cell FAILS the read that touches it, on every SQL backend, rather than
/// reading as no metadata: a degraded read hands the caller a row with no metadata, and the next write of that
/// row writes null over bytes a repair could have recovered.
/// </summary>
public sealed class MetadataCellTests
{
    private const string Corrupt = "{\"role\": ";

    [Fact]
    public void Deserialize_throws_on_a_malformed_object_and_reads_a_blank_or_non_object_as_none()
    {
        Assert.ThrowsAny<JsonException>(() => CuratedMetadataJson.Deserialize(Corrupt));
        Assert.Null(CuratedMetadataJson.Deserialize(null));
        Assert.Null(CuratedMetadataJson.Deserialize("  "));
        Assert.Null(CuratedMetadataJson.Deserialize("[1, 2]"));
    }

    [Fact]
    public async Task A_corrupt_curated_cell_fails_the_read_on_sqlite()
    {
        using var db = new TempDb();
        var store = new SqliteCuratedMemoryStore(db.Factory);
        var id = await store.AddAsync("note", "content", taskKey: "t", metadata: new Dictionary<string, string> { ["role"] = "term" });
        using (var conn = db.Factory.Open())
            await conn.ExecuteAsync("UPDATE lyntai_curated_memory SET metadata = @bad WHERE id = @id", new { bad = Corrupt, id });

        await Assert.ThrowsAnyAsync<JsonException>(() => store.GetAsync(id));
    }

    [Fact]
    public async Task A_corrupt_node_cell_fails_the_read_on_sqlite()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var id = await store.UpsertAsync(new GraphNodeWrite("e", "t", "s", "h", "content", MemoryGrade.Associative, 7, 1,
            new Dictionary<string, string> { ["role"] = "term" }));
        using (var conn = db.Factory.Open())
            await conn.ExecuteAsync("UPDATE lyntai_memory_node SET metadata = @bad WHERE id = @id", new { bad = Corrupt, id });

        await Assert.ThrowsAnyAsync<JsonException>(() => store.GetAsync("e", id));
    }
}

/// <summary>The Postgres twins; both columns are <c>TEXT</c>, so a malformed cell can exist there too.</summary>
[Collection("postgres")]
public sealed class PostgresMetadataCellTests(PostgresFixture pg)
{
    private const string Corrupt = "{\"role\": ";

    [SkippableFact]
    public async Task A_corrupt_curated_cell_fails_the_read_on_postgres()
    {
        var factory = pg.Require();
        var store = new PostgresCuratedMemoryStore(factory);
        var id = await store.AddAsync("note", "content", taskKey: $"cell-{Guid.NewGuid():N}",
            metadata: new Dictionary<string, string> { ["role"] = "term" });
        using (var conn = factory.Open())
            await conn.ExecuteAsync("UPDATE lyntai_curated_memory SET metadata = @bad WHERE id = @id", new { bad = Corrupt, id });

        await Assert.ThrowsAnyAsync<JsonException>(() => store.GetAsync(id));
    }

    [SkippableFact]
    public async Task A_corrupt_node_cell_fails_the_read_on_postgres()
    {
        var factory = pg.Require();
        var store = new PostgresMemoryGraphStore(factory);
        var id = await store.UpsertAsync(new GraphNodeWrite("e", $"cell-{Guid.NewGuid():N}", "s", "h", "content",
            MemoryGrade.Associative, 7, 1, new Dictionary<string, string> { ["role"] = "term" }));
        using (var conn = factory.Open())
            await conn.ExecuteAsync("UPDATE lyntai_memory_node SET metadata = @bad WHERE id = @id", new { bad = Corrupt, id });

        await Assert.ThrowsAnyAsync<JsonException>(() => store.GetAsync("e", id));
    }
}
