using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>UseSqliteStorage(path, migrateOnFirstUse: true) must do NO I/O at composition time and
/// migrate exactly once on the first store access.</summary>
public class DeferredMigrationTests : IDisposable
{
    private readonly string _dbPath;

    public DeferredMigrationTests()
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_test-dbs"));
        Directory.CreateDirectory(dir);
        _dbPath = Path.Combine(dir, $"deferred-{Guid.NewGuid():N}.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _dbPath, _dbPath + "-wal", _dbPath + "-shm" })
        {
            try { File.Delete(f); } catch { }
        }
    }

    [Fact]
    public void Composition_creates_no_db_file_until_first_use()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteStorage(_dbPath, migrateOnFirstUse: true));
        using var sp = services.BuildServiceProvider();

        Assert.False(File.Exists(_dbPath)); // DI composition did no I/O

        var kv = sp.GetRequiredService<IKeyValueStore>(); // resolving a store still touches nothing
        Assert.False(File.Exists(_dbPath));
    }

    [Fact]
    public async Task First_store_access_migrates_and_round_trips()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteStorage(_dbPath, migrateOnFirstUse: true));
        using var sp = services.BuildServiceProvider();

        var kv = sp.GetRequiredService<IKeyValueStore>();
        await kv.SetAsync("k", "v"); // first real access → migrates now

        Assert.True(File.Exists(_dbPath));
        Assert.Equal("v", await kv.GetAsync("k"));

        // and the schema is fully there (memory FTS recall proves the trigram migration ran)
        var memory = sp.GetRequiredService<IMemoryStore>();
        await memory.RememberAsync("t", "s", "a deferred fact");
        Assert.Single(await memory.RecallAsync("t", query: "deferred"));
    }

    [Fact]
    public async Task Migration_runs_exactly_once_under_concurrent_first_access()
    {
        var factory = new MigratingConnectionFactory(_dbPath);

        // 16 threads race to open the very first connection; the lazy migration must run once and
        // all of them must get a working, migrated connection
        var opens = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
        {
            using var conn = factory.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM lyntai_version_info";
            return Convert.ToInt64(cmd.ExecuteScalar());
        })));

        Assert.All(opens, count => Assert.Equal(9L, count)); // all migrations applied, once
    }
}
