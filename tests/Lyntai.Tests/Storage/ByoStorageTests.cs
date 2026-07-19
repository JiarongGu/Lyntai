using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>The BYO storage seams: an app supplies its own <see cref="IDbConnectionFactory"/> (owning
/// connection lifecycle) and/or owns the schema (Lyntai runs no migrations).</summary>
public class ByoStorageTests : IDisposable
{
    private readonly TempDb _db = new(); // migrated
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task App_supplied_connection_factory_is_used()
    {
        var appFactory = _db.Factory; // the app owns this (pool, lifecycle, …); already migrated
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteStorage(appFactory)); // BYO factory, no Lyntai migration
        using var sp = services.BuildServiceProvider();

        Assert.Same(appFactory, sp.GetRequiredService<IDbConnectionFactory>());

        var kv = sp.GetRequiredService<IKeyValueStore>();
        await kv.SetAsync("k", "v");
        Assert.Equal("v", await kv.GetAsync("k"));

        var memory = sp.GetRequiredService<IMemoryStore>();
        await memory.RememberAsync("t", "s", "byo factory fact");
        Assert.Single(await memory.RecallAsync("t", query: "byo"));
    }

    [Fact]
    public async Task A_custom_factory_wrapper_is_honored()
    {
        // a decorator the app might use to add its own connection setup/telemetry
        var wrapper = new CountingFactory(_db.Factory);
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p")).UseSqliteStorage(wrapper));
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<IKeyValueStore>().SetAsync("k", "v");
        Assert.True(wrapper.Opens > 0); // the app's factory actually did the work
    }

    [Fact]
    public async Task Migrate_false_leaves_schema_ownership_to_the_app()
    {
        // fresh path, migrate:false → Lyntai creates NO tables; a store op fails because the app was
        // supposed to own/provision the schema and didn't here
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_test-dbs"));
        Directory.CreateDirectory(dir);
        var freshPath = Path.Combine(dir, $"noschema-{Guid.NewGuid():N}.db");
        try
        {
            var services = new ServiceCollection();
            services.AddLyntai(b => b
                .AddProvider(_ => new FakeLlmProvider("p"))
                .UseSqliteStorage(freshPath, migrate: false));
            using var sp = services.BuildServiceProvider();

            // no lyntai_kv table → the store throws (proving Lyntai skipped migration)
            await Assert.ThrowsAnyAsync<SqliteException>(() =>
                sp.GetRequiredService<IKeyValueStore>().SetAsync("k", "v"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var f in new[] { freshPath, freshPath + "-wal", freshPath + "-shm" })
            {
                try { File.Delete(f); } catch { }
            }
        }
    }

    private sealed class CountingFactory(IDbConnectionFactory inner) : IDbConnectionFactory
    {
        public int Opens { get; private set; }
        public System.Data.Common.DbConnection Open()
        {
            Opens++;
            return inner.Open();
        }
    }
}
