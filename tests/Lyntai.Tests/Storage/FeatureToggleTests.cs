using Dapper;
using Lyntai;
using Lyntai.Cortex;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;
using Lyntai.Tests.Fakes;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>F1 (feature toggles): a DISABLED storage feature lands no table. Selective migration is
/// driven by per-migration <c>[Tags(nameof(StorageFeature.X))]</c> + the runner's active tag set.</summary>
public class FeatureToggleTests : IDisposable
{
    private readonly string _dir = System.IO.Path.GetFullPath(System.IO.Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", "_test-dbs"));
    private readonly List<string> _paths = [];

    private string FreshPath()
    {
        System.IO.Directory.CreateDirectory(_dir);
        var p = System.IO.Path.Combine(_dir, $"features-{Guid.NewGuid():N}.db");
        _paths.Add(p);
        return p;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var p in _paths)
            foreach (var f in new[] { p, p + "-wal", p + "-shm" })
                try { System.IO.File.Delete(f); } catch { /* gitignored scratch */ }
    }

    private static bool TableExists(SqliteConnectionFactory factory, string table)
    {
        using var conn = factory.Open();
        return conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@table", new { table }) > 0;
    }

    [Fact]
    public void Selective_migration_lands_only_the_selected_features_tables()
    {
        var path = FreshPath();
        MigrationRunnerService.MigrateUp(path, StorageFeature.Score | StorageFeature.Conversation);
        var factory = new SqliteConnectionFactory(path);

        Assert.True(TableExists(factory, "lyntai_score_result"));  // Score selected
        Assert.True(TableExists(factory, "lyntai_thread"));         // Conversation selected
        Assert.True(TableExists(factory, "lyntai_message"));
        Assert.False(TableExists(factory, "lyntai_kv"));            // KeyValue NOT selected → no table
        Assert.False(TableExists(factory, "lyntai_memory_entry"));  // Memory NOT selected
        Assert.False(TableExists(factory, "lyntai_job"));           // Jobs NOT selected
        Assert.True(TableExists(factory, "lyntai_version_info"));   // version table always
    }

    [Fact]
    public void All_migrates_every_feature_the_historical_default()
    {
        var path = FreshPath();
        MigrationRunnerService.MigrateUp(path); // == StorageFeature.All
        var factory = new SqliteConnectionFactory(path);

        foreach (var t in new[] { "lyntai_kv", "lyntai_thread", "lyntai_message", "lyntai_memory_entry",
            "lyntai_score_result", "lyntai_run_trace", "lyntai_prompt_version", "lyntai_job", "lyntai_curated_memory" })
            Assert.True(TableExists(factory, t), $"{t} should exist under All");
    }

    [Fact]
    public async Task UseSqliteStorage_with_a_subset_registers_only_those_stores_and_lands_only_those_tables()
    {
        var path = FreshPath();
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseSqliteStorage(path, StorageFeature.Score));
        using var sp = services.BuildServiceProvider();

        // the Score store is registered and works...
        var scores = sp.GetRequiredService<IScoreStore>();
        await scores.SaveAsync("s", [new ScoredResult("a", "A", "g", false, 0.5)]);
        Assert.Single(await scores.GetAsync("s"));

        // ...while a DISABLED domain has no store registered and no table landed
        Assert.Null(sp.GetService<IConversationStore>());
        Assert.False(TableExists(new SqliteConnectionFactory(path), "lyntai_thread"));
    }
}
