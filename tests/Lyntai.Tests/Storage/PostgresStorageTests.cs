using Lyntai;
using Lyntai.Cortex;
using Lyntai.Jobs;
using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Lyntai.Tests.Jobs;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>Integration tests for the PostgreSQL backend against a real container (Testcontainers).
/// Every test scopes to a unique key/task/session so they can share the one migrated database.
/// The whole class skips (early-return) when Docker is unavailable — see <see cref="PostgresFixture"/>.</summary>
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

    /// <summary>F1 (feature toggles): a DISABLED storage feature lands no table on Postgres. Selective
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

    private static async Task<bool> TableExists(IDbConnectionFactory factory, string table)
    {
        using var conn = factory.Open();
        return await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn,
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = @table)",
            new { table });
    }

    // ---- cross-backend contracts, run against Postgres over the shared container ----------------------
    // Each is namespaced by a unique key (Uid()) so it coexists with the other tests on the one shared,
    // migrated database. Table-wide contract methods (ScoreStoreContract Aggregate/Export;
    // CuratedMemoryStoreContract / JobStoreContract full suites) are NOT routed here — they read across the
    // whole table and would see other tests' rows on the shared container, so they stay InMemory+SQLite
    // (see those backends' *ContractTests). The session/task/id-scoped contract methods are safe here.

    [SkippableFact] public Task KeyValue_round_trip() => Pg(() => KeyValueStoreContract.Set_get_delete_round_trip(new PostgresKeyValueStore(pg.Factory), Uid()));
    [SkippableFact] public Task KeyValue_missing() => Pg(() => KeyValueStoreContract.Missing_key_returns_null(new PostgresKeyValueStore(pg.Factory), Uid()));
    [SkippableFact] public Task KeyValue_overwrite() => Pg(() => KeyValueStoreContract.Overwrite_updates_the_value(new PostgresKeyValueStore(pg.Factory), Uid())); // ON CONFLICT upsert
    [SkippableFact] public Task KeyValue_cjk() => Pg(() => KeyValueStoreContract.Cjk_value_round_trips(new PostgresKeyValueStore(pg.Factory), Uid()));

    [SkippableFact] public Task Conversation_create_get() => Pg(() => ConversationStoreContract.Create_and_get_thread(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_metadata() => Pg(() => ConversationStoreContract.Thread_metadata_round_trips_and_updates(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_mixed_events() => Pg(() => ConversationStoreContract.Appends_mixed_kind_events_with_json_payloads_in_seq_order(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_cjk() => Pg(() => ConversationStoreContract.Cjk_payload_round_trips(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_seq_metadata() => Pg(() => ConversationStoreContract.Seq_is_1_based_and_restarts_per_thread_with_guid_ids_and_per_message_metadata(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_aliases() => Pg(() => ConversationStoreContract.Role_content_aliases_map_to_kind_payload(new PostgresConversationStore(pg.Factory), Uid()));
    [SkippableFact] public Task Conversation_cascade() => Pg(() => ConversationStoreContract.Delete_thread_cascades_to_messages(new PostgresConversationStore(pg.Factory), Uid())); // FK cascade
    [SkippableFact] public Task Conversation_list_newest_first() => Pg(() => ConversationStoreContract.List_threads_returns_newest_first(new PostgresConversationStore(pg.Factory), Uid()));

    [SkippableFact] public Task Trace_save_load() => Pg(() => TraceStoreContract.Save_and_load_with_steps_totals_and_trace_id(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_resave_replaces() => Pg(() => TraceStoreContract.Saving_the_same_session_replaces_the_trace(new PostgresTraceStore(pg.Factory), Uid()));
    [SkippableFact] public Task Trace_unknown() => Pg(() => TraceStoreContract.Unknown_session_returns_null(new PostgresTraceStore(pg.Factory), Uid()));

    [SkippableFact] public Task PromptVersion_none() => Pg(() => PromptVersionStoreContract.No_version_yet_returns_null_active_and_empty_history(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_monotonic() => Pg(() => PromptVersionStoreContract.Save_creates_monotonic_versions_and_the_latest_is_active(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_history() => Pg(() => PromptVersionStoreContract.History_is_newest_first_with_exactly_one_active(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_rollback() => Pg(() => PromptVersionStoreContract.Rollback_reactivates_an_earlier_revision_without_rewriting_history(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_rollback_missing() => Pg(() => PromptVersionStoreContract.Rollback_to_a_missing_version_returns_null_and_changes_nothing(new PostgresPromptVersionStore(pg.Factory), Uid()));
    [SkippableFact] public Task PromptVersion_isolation() => Pg(() => PromptVersionStoreContract.Names_are_isolated(new PostgresPromptVersionStore(pg.Factory), Uid()));

    // ScoreStoreContract: only the session-scoped Rescore is table-safe on the shared container (Aggregate
    // and Export are table-wide → InMemory + SQLite only, as noted above).
    [SkippableFact] public Task Score_rescore() => Pg(() => ScoreStoreContract.Rescore_replaces_not_accumulates(new PostgresScoreStore(pg.Factory)));

    // MemoryStoreContract: task-scoped, so every method is safe on the shared container. A mutable clock
    // drives the TTL contract deterministically; PostgresMemoryStore is built with cap = 3 for the cap test.
    [SkippableFact] public Task Memory_token_recall() => Pg(() => MemoryStoreContract.Remember_then_recall_by_single_token_substring(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_cjk() => Pg(() => MemoryStoreContract.Cjk_substring_recall(PgMemory(), Uid())); // pg_trgm CJK substring recall
    [SkippableFact] public Task Memory_scope() => Pg(() => MemoryStoreContract.Scope_filter_applies(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_task_isolation() => Pg(() => MemoryStoreContract.Task_isolation_applies(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_dedup() => Pg(() => MemoryStoreContract.Remembering_an_identical_fact_dedups(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_scope_dedup() => Pg(() => MemoryStoreContract.Different_scopes_are_not_deduped_together(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_ttl() { var mc = new MutableClock(); return Pg(() => MemoryStoreContract.Ttl_entries_expire_from_recall_and_are_pruned(PgMemory(mc), Uid(), mc.Advance)); }
    [SkippableFact] public Task Memory_cap() => Pg(() => MemoryStoreContract.Cap_trims_to_the_newest_entries(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_forget() => Pg(() => MemoryStoreContract.Forget_clears_a_task(PgMemory(), Uid()));
    [SkippableFact] public Task Memory_fail_open() => Pg(() => MemoryStoreContract.Recall_is_fail_open_on_empty_query(PgMemory(), Uid()));

    /// <summary>Skip-guard wrapper so each contract delegator is a one-liner.</summary>
    private async Task Pg(Func<Task> body)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        await body();
    }

    private PostgresMemoryStore PgMemory(MutableClock? clock = null) =>
        new(pg.Factory, new LyntaiOptions { MemoryCapPerScope = 3, MemoryRecallLimit = 100 }, clock: (clock ?? new MutableClock()).Get);

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

    // ---- durable jobs (each test uses a UNIQUE lane so they don't collide on the shared db) -----------
    // JobStoreContract is table-wide (fixed "default" lane + ActiveLanesAsync / ListAsync(status)) so it is
    // NOT routed against the shared container — the InMemory + SQLite JobStore*Tests run the full contract.
    // These ad-hoc tests use a UNIQUE Uid() lane, covering the Postgres-specific SKIP-LOCKED claim path.

    [SkippableFact]
    public async Task Job_claim_checkpoint_complete_lifecycle()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", """{"x":1}"""));

        var job = await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(5));
        Assert.Equal(id, job!.Id);
        Assert.Equal(JobStatus.Running, job.Status);
        Assert.Equal(1, job.Attempts);

        Assert.True(await store.SaveCheckpointAsync(id, "w1", """{"step":2}"""));
        Assert.Equal("""{"step":2}""", (await store.GetAsync(id))!.Checkpoint);
        Assert.False(await store.CompleteAsync(id, "intruder")); // fenced
        Assert.True(await store.CompleteAsync(id, "w1"));
        Assert.Equal(JobStatus.Succeeded, (await store.GetAsync(id))!.Status);
    }

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

    [SkippableFact]
    public async Task Job_stale_lease_is_reclaimed()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var clock = new MutableClock();
        var store = new PostgresJobStore(pg.Factory, clock.Get);
        var lane = Uid();
        var lease = TimeSpan.FromMinutes(1);
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", lease);

        clock.Advance(lease + TimeSpan.FromSeconds(1)); // w1 presumed dead
        var reclaimed = await store.ClaimNextAsync(lane, "w2", lease);

        Assert.Equal(id, reclaimed!.Id);
        Assert.Equal("w2", reclaimed.ClaimedBy);
        Assert.Equal(2, reclaimed.Attempts);
    }

    [SkippableFact]
    public async Task Job_higher_priority_is_claimed_first()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        await store.EnqueueAsync(new JobSpec(lane, "t", "{}", Priority: 1));
        var hi = await store.EnqueueAsync(new JobSpec(lane, "t", "{}", Priority: 5));

        var claimed = await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));
        Assert.Equal(hi, claimed!.Id);
        Assert.Equal(5, claimed.Priority);
    }

    [SkippableFact]
    public async Task Job_dead_letters_and_replays()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));

        Assert.True(await store.DeadLetterAsync(id, "w1", "exhausted"));
        Assert.Equal(JobStatus.Dead, (await store.GetAsync(id))!.Status);
        Assert.Contains(await store.ListAsync(JobStatus.Dead, lane), j => j.Id == id);

        Assert.True(await store.ReplayAsync(id));
        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Pending, job!.Status);
        Assert.Equal(0, job.Attempts);
    }

    [SkippableFact]
    public async Task Job_request_cancel_flags_running_then_cancel_running_finalizes()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));

        Assert.True(await store.RequestCancelAsync(id));
        Assert.True((await store.GetAsync(id))!.CancelRequested);
        Assert.False(await store.CancelRunningAsync(id, "intruder")); // fenced
        Assert.True(await store.CancelRunningAsync(id, "w1"));
        Assert.Equal(JobStatus.Cancelled, (await store.GetAsync(id))!.Status);
    }

    [SkippableFact]
    public async Task Job_pause_holds_out_of_claims_then_resume_restores()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));

        Assert.True(await store.PauseAsync(id));                              // Pending → Paused
        Assert.Equal(JobStatus.Paused, (await store.GetAsync(id))!.Status);
        Assert.Null(await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1))); // not claimable

        Assert.True(await store.ResumeAsync(id));                            // Paused → Pending
        Assert.Equal(id, (await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1)))!.Id);
    }

    [SkippableFact]
    public async Task Curated_memory_crud_and_filters()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresCuratedMemoryStore(pg.Factory);
        var kind = Uid(); // unique kind so the shared container doesn't cross-contaminate

        var a = await store.AddAsync(kind, "term A", source: "src", enabled: true);
        await store.AddAsync(kind, "term B", enabled: false);

        var got = await store.GetAsync(a);
        Assert.Equal("term A", got!.Content);
        Assert.Equal("src", got.Source);
        Assert.True(got.Enabled);

        Assert.Equal(2, (await store.ListAsync(kind: kind)).Count);
        var enabled = await store.ListAsync(kind: kind, enabledOnly: true);
        Assert.Single(enabled);
        Assert.Equal(a, enabled[0].Id);

        // partial update: toggle enabled only, content/source untouched
        Assert.True(await store.UpdateAsync(a, enabled: false));
        var after = await store.GetAsync(a);
        Assert.False(after!.Enabled);
        Assert.Equal("term A", after.Content);
        Assert.Equal("src", after.Source);

        // "" clears the source (null = unchanged)
        Assert.True(await store.UpdateAsync(a, source: ""));
        Assert.Equal("", (await store.GetAsync(a))!.Source);

        Assert.True(await store.RemoveAsync(a));
        Assert.Null(await store.GetAsync(a));
    }

    [SkippableFact]
    public async Task Job_progress_and_steps_are_readable_while_running()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));

        Assert.True(await store.ReportProgressAsync(id, "w1", 3, 10, "phase-1"));
        Assert.True(await store.ReportStepAsync(id, "w1", "started"));
        Assert.True(await store.ReportStepAsync(id, "w1", "halfway"));

        var job = await store.GetAsync(id);
        Assert.Equal(JobStatus.Running, job!.Status);
        Assert.Equal(3, job.Progress);
        Assert.Equal(10, job.Total);
        Assert.Equal("phase-1", job.Stage);
        Assert.Equal(["started", "halfway"], JobStepLog.Parse(job.StepLog).Select(s => s.Message));

        Assert.False(await store.ReportProgressAsync(id, "intruder", 9, 10, "x")); // fenced
    }

    [SkippableFact]
    public async Task Job_concurrent_step_reports_all_land()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));

        const int n = 25; // concurrent reports must not clobber each other (the read-modify-write race)
        await Task.WhenAll(Enumerable.Range(0, n).Select(i => store.ReportStepAsync(id, "w1", $"step-{i}")));

        var messages = JobStepLog.Parse((await store.GetAsync(id))!.StepLog).Select(s => s.Message).ToList();
        Assert.Equal(n, messages.Count);
    }

    // ---- partition keys (actor-mailbox) — run the shared JobStoreContract methods against Postgres over
    // the shared container, each namespaced to a UNIQUE lane (Uid()) so the FIFO/one-at-a-time guard is
    // exercised on the SKIP-LOCKED claim path in isolation from the other tests' rows.

    [SkippableFact] public Task Job_partition_serial_fifo() => JobPg(JobStoreContract.Same_partition_serializes_and_is_fifo);
    [SkippableFact] public Task Job_partitions_parallel() => JobPg(JobStoreContract.Different_partitions_run_in_parallel);
    [SkippableFact] public Task Job_partition_priority_ignored_within() => JobPg(JobStoreContract.Priority_is_ignored_within_a_partition_but_honored_across);
    [SkippableFact] public Task Job_partition_stale_reclaim_keeps_position() => JobPg(JobStoreContract.Stale_partition_running_is_reclaimed_before_later_pending);

    /// <summary>Skip-guarded runner for a partition contract method — builds the store over a shared
    /// MutableClock (the FIFO scenarios advance it between enqueues, the reclaim scenario advances past the
    /// lease) and passes a UNIQUE lane (Uid()) so it coexists with the other tests on the shared container.</summary>
    private async Task JobPg(Func<IJobStore, MutableClock, string, Task> body)
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");
        var clock = new MutableClock();
        await body(new PostgresJobStore(pg.Factory, clock.Get), clock, Uid());
    }
}
