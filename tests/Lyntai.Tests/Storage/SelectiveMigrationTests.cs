using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Tests.Storage;

/// <summary>D12 (selective migration) at its boundary: <see cref="StorageFeature.None"/> runs no pass at all.
/// A selected subset landing only its own tables is <c>FeatureToggleTests</c>' (the Postgres leg is
/// PostgresStorageTests.Selective_migration_lands_only_the_selected_features_tables).</summary>
public sealed class SelectiveMigrationTests : IDisposable
{
    private readonly TempDbPath _db = new("selective"); // fresh, un-migrated — this test owns the schema story
    public void Dispose() => _db.Dispose();

    [Fact]
    public void None_runs_no_pass_at_all_and_creates_nothing()
    {
        // Pins what StorageFeature.None's doc now says: zero tag passes, so FluentMigrator never runs and
        // nothing lands — not even the version table. (The doc used to promise the version table, which no
        // code ever created for None; the doc was corrected rather than the behaviour, since None wires no
        // stores and a lone version table would serve nobody.)
        Assert.Empty(StorageFeatures.TagPasses(StorageFeature.None));

        MigrationRunnerService.MigrateUp(_db.Path, StorageFeature.None);
        var factory = new SqliteConnectionFactory(_db.Path);

        Assert.False(TableExists(factory, "lyntai_version_info"));
    }

    private static bool TableExists(SqliteConnectionFactory factory, string table)
    {
        using var conn = factory.Open();
        return conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table",
            new { table }) > 0;
    }
}
