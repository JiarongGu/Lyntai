using Lyntai;
using Lyntai.Cortex;
using Lyntai.Jobs;
using Lyntai.Storage.Postgres;
using Lyntai.Tests.Jobs;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>Integration tests for the PostgreSQL backend against a real container (Testcontainers).
/// Every test scopes to a unique key/task/session so they can share the one migrated database.
/// The whole class skips (early-return) when Docker is unavailable — see <see cref="PostgresFixture"/>.</summary>
[Collection("postgres")]
public sealed class PostgresStorageTests(PostgresFixture pg)
{
    private static string Uid() => Guid.NewGuid().ToString("N");
    private DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Live_postgres_connection_works()
    {
        if (!pg.Available) return;
        using var conn = pg.Factory.Open();
        var version = await Dapper.SqlMapper.QuerySingleAsync<string>(conn, "SELECT version()");
        Assert.Contains("PostgreSQL", version); // proves a real server, not a trivially-skipped test
    }

    [Fact]
    public async Task Every_object_carries_the_lyntai_prefix()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task KeyValue_round_trips_with_upsert()
    {
        if (!pg.Available) return;
        var kv = new PostgresKeyValueStore(pg.Factory);
        var key = Uid();

        await kv.SetAsync(key, "v1");
        Assert.Equal("v1", await kv.GetAsync(key));
        await kv.SetAsync(key, "v2"); // ON CONFLICT upsert
        Assert.Equal("v2", await kv.GetAsync(key));
        await kv.DeleteAsync(key);
        Assert.Null(await kv.GetAsync(key));
    }

    [Fact]
    public async Task Conversation_appends_orders_and_cascades()
    {
        if (!pg.Available) return;
        var store = new PostgresConversationStore(pg.Factory);
        var t = Uid();

        await store.CreateThreadAsync(t, "title");
        var m1 = await store.AppendMessageAsync(t, "user", "one");
        var m2 = await store.AppendMessageAsync(t, "assistant", "two");
        Assert.True(m1.Id < m2.Id); // RETURNING id monotonic

        var msgs = await store.GetMessagesAsync(t);
        Assert.Equal(["one", "two"], msgs.Select(m => m.Content));

        await store.DeleteThreadAsync(t);
        Assert.Null(await store.GetThreadAsync(t));
        Assert.Empty(await store.GetMessagesAsync(t)); // FK cascade
    }

    [Fact]
    public async Task Score_round_trips_double_and_bool_exactly()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task Trace_round_trips_with_trace_id_and_totals()
    {
        if (!pg.Available) return;
        var store = new PostgresTraceStore(pg.Factory);
        var s = Uid();

        await store.SaveAsync(new RunTrace
        {
            SessionId = s,
            Mode = "chat",
            StartedAt = _now,
            EndedAt = _now.AddMinutes(1),
            TraceId = "0af7651916cd43dd8448eb211c80319c",
            Steps =
            [
                new TraceStep { Kind = "llm", Label = "complete", InputTokens = 1200, OutputTokens = 340, CostUsd = 0.012 },
                new TraceStep { Kind = "llm", Label = "judge", InputTokens = 300, OutputTokens = 40, CostUsd = 0.003 },
            ],
        });

        var loaded = await store.GetAsync(s);
        Assert.NotNull(loaded);
        Assert.Equal("0af7651916cd43dd8448eb211c80319c", loaded.TraceId);
        Assert.Equal(1500, loaded.TotalInputTokens);
        Assert.Equal(0.015, loaded.TotalCostUsd, precision: 10);
        Assert.Equal(2, loaded.Steps.Count);
    }

    [Fact]
    public async Task Prompt_versions_history_and_rollback()
    {
        if (!pg.Available) return;
        var store = new PostgresPromptVersionStore(pg.Factory);
        var name = Uid();

        await store.SaveAsync(name, "v1", "alice");
        await store.SaveAsync(name, "v2", "bob");

        Assert.Equal(2, (await store.GetActiveAsync(name))!.Version);
        Assert.Equal([2, 1], (await store.HistoryAsync(name)).Select(v => v.Version));

        var rolled = await store.RollbackAsync(name, 1);
        Assert.Equal(1, rolled!.Version);
        Assert.Equal("v1", (await store.GetActiveAsync(name))!.Template);
        Assert.Null(await store.RollbackAsync(name, 99));
    }

    [Fact]
    public async Task Memory_dedup_ttl_cap_and_prune()
    {
        if (!pg.Available) return;
        var store = new PostgresMemoryStore(pg.Factory,
            new LyntaiOptions { MemoryCapPerScope = 3, MemoryRecallLimit = 100 }, clock: () => _now);
        var task = Uid();

        await store.RememberAsync(task, "s", "same fact");
        await store.RememberAsync(task, "s", "same fact"); // dedup
        await store.RememberAsync(task, "s", "ephemeral", ttl: TimeSpan.FromMinutes(5));
        Assert.Equal(2, (await store.RecallAsync(task)).Count);

        for (var i = 1; i <= 5; i++) await store.RememberAsync(task, "capped", $"e{i}");
        Assert.Equal(3, (await store.RecallAsync(task, scope: "capped")).Count); // cap trims oldest

        _now += TimeSpan.FromMinutes(6);
        Assert.DoesNotContain(await store.RecallAsync(task), m => m.Content == "ephemeral"); // expired
        Assert.True(await store.PruneAsync() >= 1); // reaped
    }

    [Fact]
    public async Task Memory_pg_trgm_recalls_cjk_substring()
    {
        if (!pg.Available) return;
        var store = new PostgresMemoryStore(pg.Factory,
            new LyntaiOptions { MemoryCapPerScope = 100, MemoryRecallLimit = 100 }, clock: () => _now);
        var task = Uid();

        await store.RememberAsync(task, "notes", "灵台平台负责智能代理的记忆存储");
        await store.RememberAsync(task, "notes", "另一条无关的记录");

        // a mid-phrase CJK substring — pg_trgm ILIKE matches it (the Postgres analogue of FTS5 trigram)
        var hits = await store.RecallAsync(task, query: "智能代理");
        Assert.Single(hits);
        Assert.Contains("智能代理", hits[0].Content);
    }

    // ---- durable jobs (each test uses a UNIQUE lane so they don't collide on the shared db) -----------

    [Fact]
    public async Task Job_claim_checkpoint_complete_lifecycle()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task Job_skip_locked_never_double_claims_under_concurrency()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task Job_stale_lease_is_reclaimed()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task Job_higher_priority_is_claimed_first()
    {
        if (!pg.Available) return;
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        await store.EnqueueAsync(new JobSpec(lane, "t", "{}", Priority: 1));
        var hi = await store.EnqueueAsync(new JobSpec(lane, "t", "{}", Priority: 5));

        var claimed = await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));
        Assert.Equal(hi, claimed!.Id);
        Assert.Equal(5, claimed.Priority);
    }

    [Fact]
    public async Task Job_dead_letters_and_replays()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task Job_request_cancel_flags_running_then_cancel_running_finalizes()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task Job_pause_holds_out_of_claims_then_resume_restores()
    {
        if (!pg.Available) return;
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));

        Assert.True(await store.PauseAsync(id));                              // Pending → Paused
        Assert.Equal(JobStatus.Paused, (await store.GetAsync(id))!.Status);
        Assert.Null(await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1))); // not claimable

        Assert.True(await store.ResumeAsync(id));                            // Paused → Pending
        Assert.Equal(id, (await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1)))!.Id);
    }

    [Fact]
    public async Task Curated_memory_crud_and_filters()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task Job_progress_and_steps_are_readable_while_running()
    {
        if (!pg.Available) return;
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

    [Fact]
    public async Task Job_concurrent_step_reports_all_land()
    {
        if (!pg.Available) return;
        var store = new PostgresJobStore(pg.Factory);
        var lane = Uid();
        var id = await store.EnqueueAsync(new JobSpec(lane, "t", "{}"));
        await store.ClaimNextAsync(lane, "w1", TimeSpan.FromMinutes(1));

        const int n = 25; // concurrent reports must not clobber each other (the read-modify-write race)
        await Task.WhenAll(Enumerable.Range(0, n).Select(i => store.ReportStepAsync(id, "w1", $"step-{i}")));

        var messages = JobStepLog.Parse((await store.GetAsync(id))!.StepLog).Select(s => s.Message).ToList();
        Assert.Equal(n, messages.Count);
    }
}
