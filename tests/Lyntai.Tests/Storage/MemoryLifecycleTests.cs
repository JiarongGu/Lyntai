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
    public async Task Cap_does_not_evict_live_entries_in_favor_of_expired_ones()
    {
        // regression: the cap-trim used to keep the newest @cap by id, so an expired-but-unpruned entry
        // with a higher id would be kept while a live older entry got deleted — silently losing a fact.
        var store = new SqliteMemoryStore(_db.Factory,
            new LyntaiOptions { MemoryCapPerScope = 2, MemoryRecallLimit = 100 }, clock: () => _now);
        await store.RememberAsync("t", "s", "keep-me");                            // id1, no TTL — always live
        await store.RememberAsync("t", "s", "expiring", ttl: TimeSpan.FromMinutes(5)); // id2 (cap not yet exceeded)
        _now += TimeSpan.FromMinutes(6);                                          // "expiring" now expired
        await store.RememberAsync("t", "s", "newer");                            // id3 — triggers the cap trim (3 > 2)

        // old behavior kept the newest 2 by id (newer + expired), deleting the LIVE keep-me; the fix
        // sorts the expired entry last so IT is evicted and keep-me survives
        var live = await store.RecallAsync("t");
        Assert.Contains(live, h => h.Content == "keep-me");
        Assert.Contains(live, h => h.Content == "newer");
        Assert.Equal(2, live.Count);
    }

    [Fact]
    public async Task Re_remembering_a_fact_refreshes_its_recall_recency()
    {
        // regression: recall ordered by id, so a re-remembered (deduped) fact kept its old id and
        // recalled as OLD despite its refreshed created_at, contradicting the "refreshes recency" contract.
        var store = new SqliteMemoryStore(_db.Factory,
            new LyntaiOptions { MemoryCapPerScope = 100, MemoryRecallLimit = 100 }, clock: () => _now);
        await store.RememberAsync("t", "s", "important");
        _now += TimeSpan.FromMinutes(1);
        await store.RememberAsync("t", "s", "trivial");
        _now += TimeSpan.FromMinutes(1);
        await store.RememberAsync("t", "s", "important"); // dedup refresh — should move to the top

        var hits = await store.RecallAsync("t");
        Assert.Equal("important", hits[0].Content); // most recently reinforced ⇒ first in recall
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
