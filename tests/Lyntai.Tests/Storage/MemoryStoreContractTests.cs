using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Runs the <see cref="MemoryStoreContract"/> against the InMemory backend. Cap = 3 (for the
/// cap-trim contract); a mutable clock drives deterministic TTL expiry.</summary>
public class InMemoryMemoryStoreContractTests
{
    private DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly LyntaiOptions Options = new() { MemoryCapPerScope = 3, MemoryRecallLimit = 100 };
    private InMemoryMemoryStore New() => new(Options, clock: () => _now);
    private InMemoryMemoryStore NewWith(MemoryRetentionPolicy p) =>
        new(new LyntaiOptions { MemoryRetention = p, MemoryRecallLimit = 100 }, clock: () => _now);

    [Fact] public Task Token_recall() => MemoryStoreContract.Remember_then_recall_by_single_token_substring(New(), "k");
    [Fact] public Task Cjk() => MemoryStoreContract.Cjk_substring_recall(New(), "k");
    [Fact] public Task Scope() => MemoryStoreContract.Scope_filter_applies(New(), "k");
    [Fact] public Task Task_isolation() => MemoryStoreContract.Task_isolation_applies(New(), "k");
    [Fact] public Task Dedup() => MemoryStoreContract.Remembering_an_identical_fact_dedups(New(), "k");
    [Fact] public Task Scope_dedup() => MemoryStoreContract.Different_scopes_are_not_deduped_together(New(), "k");
    [Fact] public Task Ttl() { var s = New(); return MemoryStoreContract.Ttl_entries_expire_from_recall_and_are_pruned(s, "k", by => _now += by); }
    [Fact] public Task Cap() => MemoryStoreContract.Cap_trims_to_the_newest_entries(New(), "k");
    [Fact] public Task Forget() => MemoryStoreContract.Forget_clears_a_task(New(), "k");
    [Fact] public Task Fail_open() => MemoryStoreContract.Recall_is_fail_open_on_empty_query(New(), "k");
    [Fact] public Task Lru() { var s = NewWith(MemoryRetentionPolicy.CountCap(3, MemoryEvictionMode.Lru)); return MemoryStoreContract.Lru_evicts_least_recently_recalled(s, "k", by => _now += by); }
    [Fact] public Task Lru_bare() { var s = NewWith(MemoryRetentionPolicy.CountCap(2, MemoryEvictionMode.Lru)); return MemoryStoreContract.Lru_bare_recall_does_not_refresh_recency(s, "k", by => _now += by); }
    [Fact] public Task Default_ttl() { var s = NewWith(MemoryRetentionPolicy.TimeToLive(TimeSpan.FromMinutes(5))); return MemoryStoreContract.Default_ttl_expires_entries_without_per_call_ttl(s, "k", by => _now += by); }
    [Fact] public Task Size_budget() => MemoryStoreContract.Size_budget_evicts_to_fit(NewWith(MemoryRetentionPolicy.SizeBudget(25)), "k");
    [Fact] public Task Size_budget_runes() => MemoryStoreContract.Size_budget_counts_code_points_not_utf16_units(NewWith(MemoryRetentionPolicy.SizeBudget(2)), "k");
    [Fact] public Task Manual() => MemoryStoreContract.Manual_policy_never_evicts(NewWith(MemoryRetentionPolicy.Manual), "k");
}

/// <summary>Runs the <see cref="MemoryStoreContract"/> against SQLite over a per-test temp db. Cap = 3;
/// a mutable clock drives deterministic TTL expiry.</summary>
public class SqliteMemoryStoreContractTests : IDisposable
{
    private readonly TempDb _db = new();
    private DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly LyntaiOptions Options = new() { MemoryCapPerScope = 3, MemoryRecallLimit = 100 };
    private SqliteMemoryStore New() => new(_db.Factory, Options, clock: () => _now);
    private SqliteMemoryStore NewWith(MemoryRetentionPolicy p) =>
        new(_db.Factory, new LyntaiOptions { MemoryRetention = p, MemoryRecallLimit = 100 }, clock: () => _now);

    public void Dispose() => _db.Dispose();

    [Fact] public Task Token_recall() => MemoryStoreContract.Remember_then_recall_by_single_token_substring(New(), "k");
    [Fact] public Task Cjk() => MemoryStoreContract.Cjk_substring_recall(New(), "k");
    [Fact] public Task Scope() => MemoryStoreContract.Scope_filter_applies(New(), "k");
    [Fact] public Task Task_isolation() => MemoryStoreContract.Task_isolation_applies(New(), "k");
    [Fact] public Task Dedup() => MemoryStoreContract.Remembering_an_identical_fact_dedups(New(), "k");
    [Fact] public Task Scope_dedup() => MemoryStoreContract.Different_scopes_are_not_deduped_together(New(), "k");
    [Fact] public Task Ttl() { var s = New(); return MemoryStoreContract.Ttl_entries_expire_from_recall_and_are_pruned(s, "k", by => _now += by); }
    [Fact] public Task Cap() => MemoryStoreContract.Cap_trims_to_the_newest_entries(New(), "k");
    [Fact] public Task Forget() => MemoryStoreContract.Forget_clears_a_task(New(), "k");
    [Fact] public Task Fail_open() => MemoryStoreContract.Recall_is_fail_open_on_empty_query(New(), "k");
    [Fact] public Task Lru() { var s = NewWith(MemoryRetentionPolicy.CountCap(3, MemoryEvictionMode.Lru)); return MemoryStoreContract.Lru_evicts_least_recently_recalled(s, "k", by => _now += by); }
    [Fact] public Task Lru_bare() { var s = NewWith(MemoryRetentionPolicy.CountCap(2, MemoryEvictionMode.Lru)); return MemoryStoreContract.Lru_bare_recall_does_not_refresh_recency(s, "k", by => _now += by); }
    [Fact] public Task Default_ttl() { var s = NewWith(MemoryRetentionPolicy.TimeToLive(TimeSpan.FromMinutes(5))); return MemoryStoreContract.Default_ttl_expires_entries_without_per_call_ttl(s, "k", by => _now += by); }
    [Fact] public Task Size_budget() => MemoryStoreContract.Size_budget_evicts_to_fit(NewWith(MemoryRetentionPolicy.SizeBudget(25)), "k");
    [Fact] public Task Size_budget_runes() => MemoryStoreContract.Size_budget_counts_code_points_not_utf16_units(NewWith(MemoryRetentionPolicy.SizeBudget(2)), "k");
    [Fact] public Task Manual() => MemoryStoreContract.Manual_policy_never_evicts(NewWith(MemoryRetentionPolicy.Manual), "k");
}
