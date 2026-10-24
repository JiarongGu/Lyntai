using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Every <see cref="MemoryStoreContract"/> fact as a theory case — derive with a store factory taking
/// the eviction policy and the whole contract runs on that backend (<see cref="StoreContractSuite{TStore}"/>).
/// The store reads the suite's <see cref="StoreContractSuite{TStore}.Clock"/>, which drives TTL expiry.</summary>
public abstract class MemoryStoreContractFacts : StoreContractSuite<IMemoryStore>
{
    /// <summary>A store with <paramref name="policy"/>, a recall limit of 100 and the suite's clock.</summary>
    protected abstract IMemoryStore NewWith(MemoryEvictionPolicy policy);

    protected static LyntaiOptions OptionsWith(MemoryEvictionPolicy policy) =>
        new() { MemoryEviction = policy, MemoryRecallLimit = 100 };

    protected sealed override IMemoryStore CreateStore(string fact) => NewWith(PolicyFor(fact));

    /// <summary>The policy a fact's doc names; every other fact runs under <c>CountCap(3)</c>.</summary>
    private static MemoryEvictionPolicy PolicyFor(string fact) => fact switch
    {
        nameof(MemoryStoreContract.Lru_evicts_least_recently_recalled) => MemoryEvictionPolicy.CountCap(3, MemoryEvictionMode.Lru),
        nameof(MemoryStoreContract.Lru_bare_recall_does_not_refresh_recency) => MemoryEvictionPolicy.CountCap(2, MemoryEvictionMode.Lru),
        nameof(MemoryStoreContract.Lru_recency_tie_broken_by_id) => MemoryEvictionPolicy.CountCap(2, MemoryEvictionMode.Lru),
        nameof(MemoryStoreContract.Default_ttl_expires_entries_without_per_call_ttl) => MemoryEvictionPolicy.TimeToLive(TimeSpan.FromMinutes(5)),
        nameof(MemoryStoreContract.Size_budget_evicts_to_fit) => MemoryEvictionPolicy.SizeBudget(25),
        nameof(MemoryStoreContract.Size_budget_counts_code_points_not_utf16_units) => MemoryEvictionPolicy.SizeBudget(2),
        nameof(MemoryStoreContract.Both_count_cap_and_size_budget_apply) => new MemoryEvictionPolicy { MaxEntriesPerScope = 3, MaxCharsPerScope = 25 },
        nameof(MemoryStoreContract.Manual_policy_never_evicts) => MemoryEvictionPolicy.Manual,
        _ => MemoryEvictionPolicy.CountCap(3),
    };

    [SkippableTheory, ContractFacts(typeof(MemoryStoreContract))]
    public Task Satisfies_the_contract(string fact) => RunAsync(typeof(MemoryStoreContract), fact);
}

/// <summary>The <see cref="MemoryStoreContract"/> against the InMemory backend.</summary>
public class InMemoryMemoryStoreContractTests : MemoryStoreContractFacts
{
    protected override IMemoryStore NewWith(MemoryEvictionPolicy p) => new InMemoryMemoryStore(OptionsWith(p), clock: Clock.Get);
}

/// <summary>The <see cref="MemoryStoreContract"/> against SQLite over a per-test temp db.</summary>
[ExcludesFact(nameof(MemoryStoreContract.More_matched_terms_outrank_a_newer_weaker_match),
    "SQLite ranks a recall by FTS5 bm25, not by matched-term count (docs/DECISIONS.md D55)")]
public class SqliteMemoryStoreContractTests : MemoryStoreContractFacts, IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    protected override IMemoryStore NewWith(MemoryEvictionPolicy p) => new SqliteMemoryStore(_db.Factory, OptionsWith(p), clock: Clock.Get);
}
