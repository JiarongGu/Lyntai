using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Tests.Storage;

/// <summary>D15 (selective migration): migrating a fresh db with a SINGLE StorageFeature lands ONLY that
/// domain's tables — the per-domain 1.0 baselines each carry <c>[Tags(nameof(StorageFeature.X),
/// StorageFeatures.AllTag)]</c> and the runner applies a migration only when its requested tag set is a
/// subset of the migration's tags, so a disabled domain's table never lands. Guards that the squash into
/// per-feature baselines kept each tag gating independently (the sibling Postgres leg is
/// PostgresStorageTests.Selective_migration_lands_only_the_selected_features_tables).</summary>
public sealed class SelectiveMigrationTests : IDisposable
{
    private readonly TempDbPath _db = new("selective"); // fresh, un-migrated — this test owns the schema story
    public void Dispose() => _db.Dispose();

    [Fact]
    public void Migrating_a_single_feature_lands_only_that_domains_tables()
    {
        MigrationRunnerService.MigrateUp(_db.Path, StorageFeature.Score);
        var factory = new SqliteConnectionFactory(_db.Path);

        Assert.True(TableExists(factory, "lyntai_score_result"));   // Score selected → its table lands
        Assert.False(TableExists(factory, "lyntai_kv"));            // KeyValue NOT selected → no table
        Assert.False(TableExists(factory, "lyntai_memory_entry"));  // Memory NOT selected → no table
    }

    private static bool TableExists(SqliteConnectionFactory factory, string table)
    {
        using var conn = factory.Open();
        return conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table",
            new { table }) > 0;
    }
}
