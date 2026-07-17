using Lyntai;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>The v0.4 memory lifecycle: dedup on remember, per-entry TTL, and prune. Time is driven by
/// an injected clock so expiry is deterministic (no wall-clock races).</summary>
public class MemoryLifecycleTests : IDisposable
{
    private readonly TempDb _db = new();
    private DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private readonly SqliteMemoryStore _store;

    public MemoryLifecycleTests() =>
        _store = new SqliteMemoryStore(_db.Factory,
            new LyntaiOptions { MemoryCapPerScope = 100, MemoryRecallLimit = 100 }, clock: () => _now);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Remembering_an_identical_fact_refreshes_instead_of_duplicating()
    {
        await _store.RememberAsync("task", "scope", "the same fact");
        await _store.RememberAsync("task", "scope", "the same fact");
        await _store.RememberAsync("task", "scope", "the same fact");

        var hits = await _store.RecallAsync("task");

        Assert.Single(hits); // one entry, not three
        Assert.Equal("the same fact", hits[0].Content);
    }

    [Fact]
    public async Task Different_scopes_are_not_deduped_together()
    {
        await _store.RememberAsync("task", "a", "shared text");
        await _store.RememberAsync("task", "b", "shared text");

        Assert.Equal(2, (await _store.RecallAsync("task")).Count);
    }

    [Fact]
    public async Task Expired_entries_are_not_recalled()
    {
        await _store.RememberAsync("task", "scope", "ephemeral", ttl: TimeSpan.FromMinutes(5));
        await _store.RememberAsync("task", "scope", "durable");

        Assert.Equal(2, (await _store.RecallAsync("task")).Count); // both live now

        _now += TimeSpan.FromMinutes(6); // past the ephemeral entry's TTL

        var hits = await _store.RecallAsync("task");
        Assert.Single(hits);
        Assert.Equal("durable", hits[0].Content);
    }

    [Fact]
    public async Task Expired_entries_are_excluded_from_query_recall_too()
    {
        await _store.RememberAsync("task", "scope", "the deployment pipeline is fragile", ttl: TimeSpan.FromMinutes(5));
        _now += TimeSpan.FromMinutes(6);

        var hits = await _store.RecallAsync("task", query: "deployment pipeline");
        Assert.Empty(hits); // expired → not matched even by FTS
    }

    [Fact]
    public async Task Refreshing_a_fact_extends_its_ttl()
    {
        await _store.RememberAsync("task", "scope", "keep me", ttl: TimeSpan.FromMinutes(5));
        _now += TimeSpan.FromMinutes(3);
        await _store.RememberAsync("task", "scope", "keep me", ttl: TimeSpan.FromMinutes(5)); // refresh at t=3m
        _now += TimeSpan.FromMinutes(4); // t=7m: past the original 5m expiry, within the refreshed 3m+5m=8m

        Assert.Single(await _store.RecallAsync("task")); // still alive — the refresh reset the clock
    }

    [Fact]
    public async Task Prune_reaps_expired_entries_and_reports_the_count()
    {
        await _store.RememberAsync("task", "scope", "gone soon", ttl: TimeSpan.FromMinutes(5));
        await _store.RememberAsync("task", "scope", "permanent");
        _now += TimeSpan.FromMinutes(6);

        var removed = await _store.PruneAsync();

        Assert.Equal(1, removed);
        Assert.Single(await _store.RecallAsync("task"));
    }

    [Fact]
    public async Task Prune_older_than_removes_by_age_regardless_of_ttl()
    {
        await _store.RememberAsync("task", "scope", "old fact"); // no TTL, at t=0
        _now += TimeSpan.FromMinutes(10);
        await _store.RememberAsync("task", "scope", "new fact"); // at t=10m

        var removed = await _store.PruneAsync(olderThan: TimeSpan.FromMinutes(5));

        Assert.Equal(1, removed); // "old fact" is 10m old (> 5m); "new fact" just written
        var hits = await _store.RecallAsync("task");
        Assert.Single(hits);
        Assert.Equal("new fact", hits[0].Content);
    }

    [Fact]
    public async Task Prune_can_be_scoped_to_one_task()
    {
        await _store.RememberAsync("task-a", "s", "a", ttl: TimeSpan.FromMinutes(5));
        await _store.RememberAsync("task-b", "s", "b", ttl: TimeSpan.FromMinutes(5));
        _now += TimeSpan.FromMinutes(6);

        var removed = await _store.PruneAsync(taskKey: "task-a");

        Assert.Equal(1, removed);
        Assert.Empty(await _store.RecallAsync("task-a"));
        // task-b's expired entry survives the scoped prune (though recall still filters it)
        using var conn = _db.Factory.Open();
        Assert.Equal(1L, Dapper.SqlMapper.ExecuteScalar<long>(conn,
            "SELECT COUNT(*) FROM lyntai_memory_entry WHERE task_key = 'task-b'"));
    }
}
