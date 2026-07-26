using Lyntai;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>The v0.4 memory lifecycle against SQLite. Time is driven by an injected clock so expiry is
/// deterministic (no wall-clock races). The contract-covered behaviors (dedup on remember, scope
/// isolation, TTL expiry/refresh, recency refresh, scoped/olderThan prune) live in
/// <see cref="MemoryStoreContract"/> (<see cref="SqliteMemoryStoreContractTests"/> + the InMemory/Postgres
/// classes — T9 promoted the lifecycle semantics there); this file keeps only the SQLite-specific
/// regressions (FTS-path expiry filtering, prune count accounting, cap-vs-expired eviction).</summary>
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
    public async Task Expired_entries_are_excluded_from_query_recall_too()
    {
        await _store.RememberAsync("task", "scope", "the deployment pipeline is fragile", ttl: TimeSpan.FromMinutes(5));
        _now += TimeSpan.FromMinutes(6);

        var hits = await _store.RecallAsync("task", query: "deployment pipeline");
        Assert.Empty(hits); // expired → not matched even by FTS
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

}
