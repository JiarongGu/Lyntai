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

    // A kind holding U+0000 is the failure these two inject: the server refuses a NUL in text, so the edge
    // statement throws AFTER the earlier statements of the same batch have run.
    private const string RefusedKind = "refused\0kind";

    /// <summary>A batch of edges is one unit: a statement failing partway leaves none of the batch — the
    /// edges before it as well as the one that failed. Each edge was its own autocommit statement, so the
    /// first one survived.</summary>
    [SkippableFact]
    public async Task A_batch_of_edges_that_fails_partway_commits_none_of_them()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var (engine, key) = (Uid(), Uid());
        var a = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", "a", "alpha", MemoryGrade.Associative, 7, 1, null));
        var b = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", "b", "beta", MemoryGrade.Associative, 7, 1, null));

        await Assert.ThrowsAnyAsync<Exception>(() => store.LinkManyAsync(engine,
        [
            new GraphEdgeWrite(a, b, "kept", 1, Symmetric: true),
            new GraphEdgeWrite(a, b, RefusedKind, 1),
        ]));

        Assert.Empty(await store.NeighboursAsync(engine, key, [a], 10));
        Assert.Empty(await store.NeighboursAsync(engine, key, [b], 10));
    }

    /// <summary>A recall's touches and edges commit together: the edge failing leaves the touch unapplied.
    /// The reviews stay outside the transaction, which the shared contract's write-back facts hold.</summary>
    [SkippableFact]
    public async Task A_write_back_whose_edge_fails_leaves_its_touches_unapplied()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var (engine, key) = (Uid(), Uid());
        var a = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", "a", "alpha", MemoryGrade.Associative, 7, 1, null));
        var b = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", "b", "beta", MemoryGrade.Associative, 7, 1, null));

        await Assert.ThrowsAnyAsync<Exception>(() => store.WriteBackAsync(engine, new GraphWriteBack(
            [new GraphTouch(a, 10.5)],
            [new GraphEdgeWrite(a, b, "kept", 1), new GraphEdgeWrite(a, b, RefusedKind, 1)],
            [], 0)));

        var node = await store.GetAsync(engine, a);
        Assert.NotNull(node);
        Assert.Equal(0, node!.RecallCount);
        Assert.Equal(7, node.Stability, precision: 6);
        Assert.Empty(await store.NeighboursAsync(engine, key, [a], 10));
    }

    // Nodes per round and rounds before the test gives up looking for a deadlock. Opposite orders over 40 nodes
    // cross almost every round on the unordered store; the server's deadlock detector fires after
    // deadlock_timeout (1 s), so a failing run ends at the first one.
    private const int RaceNodes = 40, RaceRounds = 25;

    private async Task<long[]> SeedRaceNodesAsync(PostgresMemoryGraphStore store, string engine, string key)
    {
        var ids = new long[RaceNodes];
        for (var i = 0; i < ids.Length; i++)
            ids[i] = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", $"n{i}", $"node {i}",
                MemoryGrade.Associative, 7, 1, null));
        return ids;
    }

    /// <summary>Runs <paramref name="ascending"/> and <paramref name="descending"/> together, round after round,
    /// until one of them is aborted or the rounds run out; returns the rounds it took and what failed.</summary>
    private static async Task<(int Rounds, Exception[] Failures)> RaceAsync(
        Func<Task> ascending, Func<Task> descending)
    {
        for (var round = 1; round <= RaceRounds; round++)
        {
            using var gate = new Barrier(2);
            Task Run(Func<Task> work) => Task.Run(() => { gate.SignalAndWait(); return work(); });
            var both = new[] { Run(ascending), Run(descending) };
            try { await Task.WhenAll(both); }
            catch { /* read per task below */ }
            var failures = both.Where(t => t.IsFaulted).Select(t => t.Exception!.GetBaseException()).ToArray();
            if (failures.Length > 0) return (round, failures);
        }
        return (RaceRounds, []);
    }

    /// <summary>Two write-backs over the same nodes and edges, listed in opposite orders, never deadlock: the
    /// transaction holds each row it touched until commit, so the order the rows are taken in is what keeps
    /// two of them from each waiting on the other. The store takes them in one fixed order.</summary>
    [SkippableFact]
    public async Task Concurrent_write_backs_listing_the_same_nodes_in_opposite_orders_do_not_deadlock()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var (engine, key) = (Uid(), Uid());
        var ids = await SeedRaceNodesAsync(store, engine, key);
        GraphWriteBack WriteBack(IEnumerable<long> order) => new(
            [.. order.Select(id => new GraphTouch(id, 10.5))],
            [.. order.Zip(order.Skip(1), (from, to) => new GraphEdgeWrite(from, to, "race", 1, Symmetric: true))],
            [], 0);
        var forward = WriteBack(ids);
        var backward = WriteBack(ids.Reverse());

        var (rounds, failures) = await RaceAsync(
            () => store.WriteBackAsync(engine, forward), () => store.WriteBackAsync(engine, backward));

        Assert.True(failures.Length == 0, $"after {rounds} round(s): {string.Join(" | ", failures.Select(f => f.Message))}");
        // every round wrote both write-backs whole
        var node = await store.GetAsync(engine, ids[0]);
        Assert.Equal(2 * RaceRounds, node!.RecallCount);
    }

    /// <summary>The same for two batches of edges, which take the edge rows (and, for a new edge, the unique
    /// index entry) in the order listed.</summary>
    [SkippableFact]
    public async Task Concurrent_edge_batches_listing_the_same_edges_in_opposite_orders_do_not_deadlock()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var (engine, key) = (Uid(), Uid());
        var ids = await SeedRaceNodesAsync(store, engine, key);
        var forward = ids.Zip(ids.Skip(1), (from, to) => new GraphEdgeWrite(from, to, "race", 1, Symmetric: true)).ToList();
        var backward = Enumerable.Reverse(forward).ToList();

        var (rounds, failures) = await RaceAsync(
            () => store.LinkManyAsync(engine, forward), () => store.LinkManyAsync(engine, backward));

        Assert.True(failures.Length == 0, $"after {rounds} round(s): {string.Join(" | ", failures.Select(f => f.Message))}");
        var neighbours = await store.NeighboursAsync(engine, key, [ids[0]], 10);
        Assert.Equal(2 * RaceRounds, neighbours.Single().EdgeWeight, precision: 6);
    }

    /// <summary>Taking the rows in a fixed order changes no outcome: a node touched twice in one batch ends on
    /// the LAST listed touch with both counted, and an edge listed twice carries both weights.</summary>
    [SkippableFact]
    public async Task A_batch_listing_a_node_or_an_edge_twice_writes_the_same_rows_as_before()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresMemoryGraphStore(pg.Factory);
        var (engine, key) = (Uid(), Uid());
        var a = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", "a", "alpha", MemoryGrade.Associative, 7, 1, null));
        var b = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", "b", "beta", MemoryGrade.Associative, 7, 1, null));

        await store.WriteBackAsync(engine, new GraphWriteBack(
            [new GraphTouch(b, 3.5), new GraphTouch(a, 11.5), new GraphTouch(b, 4.5), new GraphTouch(a, 12.5)],
            [new GraphEdgeWrite(b, a, "k", 2), new GraphEdgeWrite(a, b, "k", 1), new GraphEdgeWrite(b, a, "k", 4)],
            [], 0));

        var nodeA = await store.GetAsync(engine, a);
        var nodeB = await store.GetAsync(engine, b);
        Assert.Equal((2, 12.5), (nodeA!.RecallCount, nodeA.Stability));
        Assert.Equal((2, 4.5), (nodeB!.RecallCount, nodeB.Stability));
        Assert.Equal(1, (await store.NeighboursAsync(engine, key, [a], 10)).Single().EdgeWeight, precision: 6);
        Assert.Equal(6, (await store.NeighboursAsync(engine, key, [b], 10)).Single().EdgeWeight, precision: 6);
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
