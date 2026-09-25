using Dapper;
using Lyntai;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>Two SQL wirings in one container, split by feature — the "mixable per domain" the README
/// advertises. Every store must run over ITS OWN wiring's connection factory; the stores register first-wins,
/// so a factory resolved from the container at run time (last-wins) hands the first wiring's stores the
/// second wiring's database.</summary>
public sealed class SplitStorageWiringTests : IDisposable
{
    private readonly TempDbPath _memoryDb = new("split-memory");
    private readonly TempDbPath _kvDb = new("split-kv");

    public void Dispose()
    {
        _memoryDb.Dispose();
        _kvDb.Dispose();
    }

    private static long Rows(string path, string table)
    {
        using var conn = new SqliteConnectionFactory(path).Open();
        var exists = conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table", new { table });
        return exists == 0 ? -1 : conn.ExecuteScalar<long>($"SELECT COUNT(*) FROM {table}");
    }

    [Fact]
    public async Task Two_sqlite_files_split_by_feature_each_keep_their_own_rows()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseSqliteStorage(_memoryDb.Path, StorageFeature.Memory)
            .UseSqliteStorage(_kvDb.Path, StorageFeature.KeyValue));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<IMemoryStore>().RememberAsync("t", "s", "a fact for the memory file");
        await sp.GetRequiredService<IMemoryGraphStore>().UpsertAsync(
            new GraphNodeWrite("e", "t", "s", "h", "a node for the memory file", MemoryGrade.Associative, 7, 1, null));
        await sp.GetRequiredService<IKeyValueStore>().SetAsync("k", "a value for the kv file");

        Assert.Equal(1, Rows(_memoryDb.Path, "lyntai_memory_entry"));
        Assert.Equal(1, Rows(_memoryDb.Path, "lyntai_memory_node"));
        Assert.Equal(-1, Rows(_memoryDb.Path, "lyntai_kv"));           // Memory only: no kv table at all
        Assert.Equal(1, Rows(_kvDb.Path, "lyntai_kv"));
        Assert.Equal(-1, Rows(_kvDb.Path, "lyntai_memory_entry"));
    }

    [Fact]
    public async Task A_governance_helper_binds_to_its_own_backends_wiring()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseSqliteStorage(_memoryDb.Path)
            .UseSqliteVectorStore()
            .UseSqliteStorage(_kvDb.Path, StorageFeature.KeyValue | StorageFeature.Governance));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<IVectorStore>().UpsertAsync("c", "1", [1f, 0f], "payload");

        // the helper binds to the LAST wiring of its own backend, the same one its Governance check reads
        Assert.Equal(1, Rows(_kvDb.Path, "lyntai_vector"));
        Assert.Equal(0, Rows(_memoryDb.Path, "lyntai_vector"));
    }
}

/// <summary>The same split across the two relational backends — a SQLite store sent over an Npgsql
/// connection is the worst case of the cross-wiring, since the SQL is the wrong dialect entirely.</summary>
[Collection("postgres")]
public sealed class SplitStorageAcrossBackendsTests(PostgresFixture pg) : IDisposable
{
    private readonly TempDbPath _sqlite = new("split-cross");

    public void Dispose() => _sqlite.Dispose();

    [SkippableFact]
    public async Task Sqlite_and_postgres_split_by_feature_each_run_their_own_sql()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var key = Guid.NewGuid().ToString("N");
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseSqliteStorage(_sqlite.Path, StorageFeature.Memory | StorageFeature.Governance)
            .UseSqliteVectorStore()
            .UsePostgresStorage(pg.ConnectionString, StorageFeature.KeyValue, SchemaMigration.None));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<IMemoryStore>().RememberAsync(key, "s", "a fact for sqlite");
        await sp.GetRequiredService<IVectorStore>().UpsertAsync(key, "1", [1f, 0f], "payload");
        await sp.GetRequiredService<IKeyValueStore>().SetAsync(key, "a value for postgres");

        using (var conn = new SqliteConnectionFactory(_sqlite.Path).Open())
        {
            Assert.Equal(1, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM lyntai_memory_entry WHERE task_key = @key", new { key }));
            Assert.Equal(1, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM lyntai_vector WHERE collection = @key", new { key }));
        }
        using (var conn = pg.Factory.Open())
            Assert.Equal(1, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM lyntai_kv WHERE key = @key", new { key }));
    }
}
