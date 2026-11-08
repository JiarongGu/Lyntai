using Dapper;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Npgsql;

namespace Lyntai.Tests.Storage;

/// <summary>The Postgres side of <see cref="ConcurrentMigrationTests"/>: runners started together on a FRESH
/// database — its own, created here, so the shared fixture's already-migrated one cannot hide the race.</summary>
[Collection("postgres")]
public sealed class PostgresConcurrentMigrationTests(PostgresFixture pg)
{
    private const int Runners = 4;

    [SkippableFact]
    public async Task Runners_started_together_on_a_fresh_database_all_succeed()
    {
        pg.Require();
        var cs = await FreshDatabaseAsync();
        using var start = new Barrier(Runners);

        await Task.WhenAll(Enumerable.Range(0, Runners).Select(_ => Task.Run(() =>
        {
            start.SignalAndWait();
            MigrationRunnerService.MigrateUp(cs);
        })));

        await using var conn = new NpgsqlConnection(cs);
        Assert.Equal(SchemaFacts.PostgresVersions,
            (await conn.QueryAsync<long>("SELECT version FROM lyntai_version_info ORDER BY version")).ToList());
    }

    [SkippableFact]
    public async Task Vector_stores_started_together_on_a_fresh_database_all_create_the_schema()
    {
        pg.Require();
        var cs = await FreshDatabaseAsync();
        using var start = new Barrier(Runners);

        await Task.WhenAll(Enumerable.Range(0, Runners).Select(_ => Task.Run(async () =>
        {
            var store = new PostgresVectorStore(new PostgresConnectionFactory(cs));
            start.SignalAndWait();
            await store.ListCollectionsAsync("");
        })));
    }

    private async Task<string> FreshDatabaseAsync()
    {
        var name = "lyntai_race_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(pg.ConnectionString))
            await admin.ExecuteAsync($"CREATE DATABASE {name}");
        return new NpgsqlConnectionStringBuilder(pg.ConnectionString) { Database = name, Pooling = false }.ToString();
    }
}
