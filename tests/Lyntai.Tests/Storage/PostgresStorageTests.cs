using Lyntai;
using Lyntai.Cortex;
using Lyntai.Storage.Postgres;
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
}
