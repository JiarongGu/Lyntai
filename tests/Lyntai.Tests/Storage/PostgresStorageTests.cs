using Lyntai;
using Lyntai.Cortex;
using Lyntai.Jobs;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Jobs;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>Integration tests for the PostgreSQL backend against a real container (Testcontainers).
/// Every test scopes to a unique key/task/session so they can share the one migrated database.
/// Every test SKIPS (visibly, never a pass) when Docker is unavailable — see <see cref="PostgresFixture"/>.</summary>
[Collection("postgres")]
public sealed class PostgresStorageTests(PostgresFixture pg)
{
    private static string Uid() => Guid.NewGuid().ToString("N");

    [SkippableFact]
    public async Task Live_postgres_connection_works()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        using var conn = pg.Factory.Open();
        var version = await Dapper.SqlMapper.QuerySingleAsync<string>(conn, "SELECT version()");
        Assert.Contains("PostgreSQL", version); // proves a real server, not a trivially-skipped test
    }

    /// <summary>Every <see cref="Lyntai.Tests.Memory.MemoryGraphStoreContract"/> fact against a real
    /// Postgres, driven from the shared theory source so coverage is STRUCTURAL rather than counted.</summary>
    /// <remarks>
    /// <para>A counted <c>covered</c> literal cannot see the case that matters most: a fact wired to
    /// Postgres ALONE passes once the literal is bumped, while InMemory and SQLite silently never run it.</para>
    /// <para>One container: the fixture is shared across the whole <c>postgres</c> collection, and xUnit
    /// runs a class's cases sequentially, so startup is paid once. Each case takes a fresh
    /// <see cref="Uid"/> because the database is shared.</para>
    /// </remarks>
    [SkippableTheory]
    [MemberData(nameof(Lyntai.Tests.Memory.MemoryGraphStoreFacts.Names),
        MemberType = typeof(Lyntai.Tests.Memory.MemoryGraphStoreFacts))]
    public Task Graph_store_satisfies_the_contract(string fact)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        return Lyntai.Tests.Memory.MemoryGraphStoreFacts.RunAsync(
            fact, clock => new PostgresMemoryGraphStore(pg.Factory, clock: clock), Uid());
    }

    /// <summary>The write path's own finiteness guard, pinned as ACTUAL COLUMN CONTENT — the same fact
    /// <c>SqliteMemoryGraphStoreTests</c> pins for SQLite. <c>PostgresMemoryGraphStore.UpsertAsync</c> reads
    /// the RAW incoming <see cref="MemorySignals"/> bag a second time, straight into the promoted
    /// <c>salience</c> column, bypassing <see cref="MemorySignalsJson.Serialize"/>'s own finiteness filter —
    /// and unlike SQLite, Npgsql accepts a NaN <c>double</c> parameter without complaint, so the corruption
    /// here is silent rather than a thrown exception: the row would order wherever Postgres's NaN comparison
    /// semantics happen to put it in <c>ORDER BY … n.salience DESC …</c>, undermining the "no row silently
    /// mis-sorts" invariant the migration's own comment argues for.</summary>
    [SkippableFact]
    public async Task A_non_finite_judged_salience_writes_the_neutral_column_value_not_NaN()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var key = Uid();
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, double.NaN);

        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a non-finite judgement",
            MemoryGrade.Associative, 7, 1, null, signals));

        using var conn = pg.Factory.Open();
        var salience = await Dapper.SqlMapper.ExecuteScalarAsync<double>(conn,
            "SELECT salience FROM lyntai_memory_node WHERE task_key = @key", new { key });
        Assert.False(double.IsNaN(salience), $"the salience column holds {salience}, not the neutral value");
        Assert.Equal(1, salience);
    }

    /// <summary>The write path's own finiteness guard for <c>difficulty</c>, pinned as ACTUAL COLUMN CONTENT — the same shape
    /// <see cref="A_non_finite_judged_salience_writes_the_neutral_column_value_not_NaN"/> pins for
    /// <c>salience</c>, and for the identical reason: Npgsql binds a NaN <c>double</c> without complaint.</summary>
    [SkippableFact]
    public async Task A_non_finite_judged_difficulty_writes_the_neutral_column_value_not_NaN()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var key = Uid();
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, double.NaN);

        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a non-finite judgement",
            MemoryGrade.Associative, 7, 1, null, signals));

        using var conn = pg.Factory.Open();
        var difficulty = await Dapper.SqlMapper.ExecuteScalarAsync<double>(conn,
            "SELECT difficulty FROM lyntai_memory_node WHERE task_key = @key", new { key });
        Assert.False(double.IsNaN(difficulty), $"the difficulty column holds {difficulty}, not the neutral value");
        Assert.Equal(5, difficulty); // the neutral mid-point, not the floor 1 — the SQLite twin's code path,
                                     // MemorySignals.Difficulty
    }

    [SkippableFact]
    public async Task Every_object_carries_the_lyntai_prefix()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        using var conn = pg.Factory.Open();
        // the package may live inside a consumer's existing db — nothing unprefixed allowed
        var stray = (await Dapper.SqlMapper.QueryAsync<string>(conn, """
            SELECT tablename FROM pg_tables WHERE schemaname = 'public' AND tablename NOT LIKE 'lyntai\_%'
            UNION ALL
            SELECT sequencename FROM pg_sequences WHERE schemaname = 'public' AND sequencename NOT LIKE 'lyntai\_%'
            UNION ALL
            SELECT indexname FROM pg_indexes WHERE schemaname = 'public'
              AND indexname NOT LIKE 'lyntai\_%' AND indexname NOT LIKE 'ix\_lyntai\_%' AND indexname NOT LIKE 'ux\_lyntai\_%'
            """)).ToList();
        Assert.Empty(stray);
    }

    /// <summary>Feature toggles: a DISABLED storage feature lands no table on Postgres. Selective
    /// migration is driven by per-migration <c>[Tags(nameof(StorageFeature.X), StorageFeatures.AllTag)]</c>
    /// + the runner's active tag set, exactly as SQLite. Uses a THROWAWAY container (not the shared,
    /// already-all-migrated fixture db) so the subset migration is observed in isolation.</summary>
    [SkippableFact]
    public async Task Selective_migration_lands_only_the_selected_features_tables()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");

        await using var container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
        await container.StartAsync();
        var cs = container.GetConnectionString();

        MigrationRunnerService.MigrateUp(cs, StorageFeature.Score | StorageFeature.Conversation);
        var factory = new PostgresConnectionFactory(cs);

        Assert.True(await TableExists(factory, "lyntai_score_result")); // Score selected
        Assert.True(await TableExists(factory, "lyntai_thread"));        // Conversation selected
        Assert.True(await TableExists(factory, "lyntai_message"));
        Assert.False(await TableExists(factory, "lyntai_kv"));           // KeyValue NOT selected → no table
        Assert.False(await TableExists(factory, "lyntai_memory_entry")); // Memory NOT selected
        Assert.False(await TableExists(factory, "lyntai_job"));          // Jobs NOT selected
        Assert.True(await TableExists(factory, "lyntai_version_info"));  // version table always
    }

    /// <summary>The awaitable twin lands the same schema on Postgres — run against the already-migrated
    /// shared fixture db, so it also pins that a second pass is a no-op. Cancellation semantics (the token
    /// honoured before any work) are covered without Docker in <see cref="AsyncMigrationTests"/>.</summary>
    [SkippableFact]
    public async Task MigrateUpAsync_is_idempotent_against_a_migrated_database()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");

        await MigrationRunnerService.MigrateUpAsync(pg.ConnectionString);

        using var conn = pg.Factory.Open();
        var applied = (await Dapper.SqlMapper.QueryAsync<long>(conn,
            "SELECT version FROM lyntai_version_info ORDER BY version")).ToList();
        Assert.Equal(SchemaFacts.PostgresVersions, applied);
    }

    private static Task<bool> TableExists(IDbConnectionFactory factory, string table) =>
        SchemaFacts.PostgresTableExists(factory, table);

    [SkippableFact]
    public async Task Score_round_trips_double_and_bool_exactly()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresScoreStore(pg.Factory);
        var s = Uid();

        await store.SaveAsync(s,
        [
            new ScoredResult("outcome", "Outcome", "deterministic", false, 0.123456789, "close"),
            new ScoredResult("judge", "Judge", "llm", true, 1.0, null),
        ]);

        var results = await store.GetAsync(s);
        Assert.Equal(2, results.Count);
        Assert.Equal(0.123456789, results[0].Score); // double precision, exact
        Assert.False(results[0].IsLlm);               // native boolean
        Assert.True(results[1].IsLlm);
        Assert.Equal(1.0, results[1].Score);
    }

    /// <summary>The one real concurrency test on this backend: 40 claimers over real async Npgsql
    /// connections, so <c>FOR UPDATE SKIP LOCKED</c> is what keeps two of them off one row.</summary>
    [SkippableFact]
    public async Task Job_skip_locked_never_double_claims_under_concurrency()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        const int n = 20;
        for (var i = 0; i < n; i++) await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));

        var claims = await Task.WhenAll(Enumerable.Range(0, n * 2)
            .Select(i => store.ClaimNextAsync(lane, $"w{i}", TimeSpan.FromMinutes(5))));

        var ids = claims.Where(j => j is not null).Select(j => j!.Id).ToList();
        Assert.Equal(n, ids.Count);              // FOR UPDATE SKIP LOCKED gave each job to exactly one
        Assert.Equal(n, ids.Distinct().Count());
    }
}
