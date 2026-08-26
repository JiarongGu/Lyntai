using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="MemoryStoreContract"/> method as a [Fact] — derive with the two store
/// factories (default options = cap 3 / recall 100, and a policy-tuned variant) and the whole contract
/// runs on that backend automatically (T11: no silent skips). The mutable <see cref="Now"/> drives
/// deterministic TTL expiry. Postgres deliberately does NOT derive: it runs the Uid-namespaced subset
/// on the shared container.</summary>
public abstract class MemoryStoreContractFacts
{
    protected DateTimeOffset Now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    protected static readonly LyntaiOptions Options = new() { MemoryCapPerScope = 3, MemoryRecallLimit = 100 };

    protected abstract IMemoryStore New();
    protected abstract IMemoryStore NewWith(MemoryEvictionPolicy p);
    private void Advance(TimeSpan by) => Now += by;

    [Fact] public Task Token_recall() => MemoryStoreContract.Remember_then_recall_by_single_token_substring(New(), "k");
    [Fact] public Task Cjk() => MemoryStoreContract.Cjk_substring_recall(New(), "k");
    [Fact] public Task Scope() => MemoryStoreContract.Scope_filter_applies(New(), "k");
    [Fact] public Task Task_isolation() => MemoryStoreContract.Task_isolation_applies(New(), "k");
    [Fact] public Task Dedup() => MemoryStoreContract.Remembering_an_identical_fact_dedups(New(), "k");
    [Fact] public Task Scope_dedup() => MemoryStoreContract.Different_scopes_are_not_deduped_together(New(), "k");
    [Fact] public Task Ttl() => MemoryStoreContract.Ttl_entries_expire_from_recall_and_are_pruned(New(), "k", Advance);
    [Fact] public Task Ttl_refresh() => MemoryStoreContract.Refreshing_a_fact_extends_its_ttl(New(), "k", Advance);
    [Fact] public Task Ttl_unstated_replaces() => MemoryStoreContract.Re_remembering_without_a_ttl_replaces_an_explicit_one(New(), "k", Advance);
    [Fact] public Task Recency_refresh() => MemoryStoreContract.Re_remembering_refreshes_recall_recency(New(), "k", Advance);
    [Fact] public Task Prune_by_age() => MemoryStoreContract.Prune_older_than_removes_by_age_within_a_task(New(), "k", Advance);
    [Fact] public Task Prune_scoped() => MemoryStoreContract.Prune_scoped_to_one_task_leaves_the_sibling(New(), "k", Advance);
    [Fact] public Task Cap() => MemoryStoreContract.Cap_trims_to_the_newest_entries(New(), "k");
    [Fact] public Task Limit_scope() => MemoryStoreContract.Limit_caps_results_and_composes_with_scope(New(), "k");
    [Fact] public Task Forget() => MemoryStoreContract.Forget_clears_a_task(New(), "k");
    [Fact] public Task Forget_scoped() => MemoryStoreContract.Forget_scoped_clears_only_that_scope(New(), "k");
    [Fact] public Task Fail_open() => MemoryStoreContract.Recall_is_fail_open_on_empty_query(New(), "k");
    [Fact] public Task Lru() => MemoryStoreContract.Lru_evicts_least_recently_recalled(NewWith(MemoryEvictionPolicy.CountCap(3, MemoryEvictionMode.Lru)), "k", Advance);
    [Fact] public Task Lru_bare() => MemoryStoreContract.Lru_bare_recall_does_not_refresh_recency(NewWith(MemoryEvictionPolicy.CountCap(2, MemoryEvictionMode.Lru)), "k", Advance);
    [Fact] public Task Default_ttl() => MemoryStoreContract.Default_ttl_expires_entries_without_per_call_ttl(NewWith(MemoryEvictionPolicy.TimeToLive(TimeSpan.FromMinutes(5))), "k", Advance);
    [Fact] public Task Size_budget() => MemoryStoreContract.Size_budget_evicts_to_fit(NewWith(MemoryEvictionPolicy.SizeBudget(25)), "k");
    [Fact] public Task Size_budget_runes() => MemoryStoreContract.Size_budget_counts_code_points_not_utf16_units(NewWith(MemoryEvictionPolicy.SizeBudget(2)), "k");
    [Fact] public Task Both_bounds() => MemoryStoreContract.Both_count_cap_and_size_budget_apply(NewWith(new MemoryEvictionPolicy { MaxEntriesPerScope = 3, MaxCharsPerScope = 25 }), "k");
    [Fact] public Task Lru_tie() => MemoryStoreContract.Lru_recency_tie_broken_by_id(NewWith(MemoryEvictionPolicy.CountCap(2, MemoryEvictionMode.Lru)), "k");
    [Fact] public Task Manual() => MemoryStoreContract.Manual_policy_never_evicts(NewWith(MemoryEvictionPolicy.Manual), "k");
}

/// <summary>The <see cref="MemoryStoreContract"/> against the InMemory backend.</summary>
public class InMemoryMemoryStoreContractTests : MemoryStoreContractFacts
{
    protected override IMemoryStore New() => new InMemoryMemoryStore(Options, clock: () => Now);
    protected override IMemoryStore NewWith(MemoryEvictionPolicy p) =>
        new InMemoryMemoryStore(new LyntaiOptions { MemoryEviction = p, MemoryRecallLimit = 100 }, clock: () => Now);
}

/// <summary>The <see cref="MemoryStoreContract"/> against SQLite over a per-test temp db.</summary>
public class SqliteMemoryStoreContractTests : MemoryStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    protected override IMemoryStore New() => new SqliteMemoryStore(_db.Factory, Options, clock: () => Now);
    protected override IMemoryStore NewWith(MemoryEvictionPolicy p) =>
        new SqliteMemoryStore(_db.Factory, new LyntaiOptions { MemoryEviction = p, MemoryRecallLimit = 100 }, clock: () => Now);
}
