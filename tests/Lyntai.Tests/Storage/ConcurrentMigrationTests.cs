using Dapper;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Tests.Storage;

/// <summary>Two migrators started together against one EMPTY database — the second to read the version table
/// sees every migration unapplied, so without a lock around the passes its DDL meets the first one's tables.</summary>
public sealed class ConcurrentMigrationTests
{
    private const int Runners = 4;
    private const int Rounds = 5;

    [Fact]
    public async Task Runners_started_together_on_a_fresh_database_all_succeed_and_apply_each_migration_once()
    {
        for (var round = 0; round < Rounds; round++)
        {
            using var db = new TempDbPath("concurrent");
            using var start = new Barrier(Runners);

            var runners = Enumerable.Range(0, Runners).Select(_ => Task.Run(() =>
            {
                start.SignalAndWait();
                MigrationRunnerService.MigrateUp(db.Path);
            })).ToArray();
            await Task.WhenAll(runners); // a loser's DDL throwing surfaces here

            using var conn = new SqliteConnectionFactory(db.Path).Open();
            Assert.Equal(SchemaFacts.SqliteVersions,
                conn.Query<long>("SELECT Version FROM lyntai_version_info ORDER BY Version").ToList());
        }
    }

    [Fact]
    public async Task The_awaitable_twin_takes_the_same_lock()
    {
        using var db = new TempDbPath("concurrent-async");
        using var start = new Barrier(Runners);

        await Task.WhenAll(Enumerable.Range(0, Runners).Select(_ => Task.Run(async () =>
        {
            start.SignalAndWait();
            await MigrationRunnerService.MigrateUpAsync(db.Path);
        })));

        using var conn = new SqliteConnectionFactory(db.Path).Open();
        Assert.Equal(SchemaFacts.SqliteVersions.Length, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM lyntai_version_info"));
    }
}
