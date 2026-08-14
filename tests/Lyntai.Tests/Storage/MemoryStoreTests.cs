using Dapper;
using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>SQLite-SPECIFIC memory-store concerns. The cross-backend semantics (single-token substring
/// recall, CJK substring, scope/task filtering, dedup, TTL, cap COUNT/membership, forget, fail-open) are
/// pinned by <see cref="MemoryStoreContract"/> (<see cref="SqliteMemoryStoreContractTests"/>). What stays
/// here is what is DELIBERATELY divergent on SQLite: the FTS→LIKE short-query fallback, and the recency
/// SEQUENCE of no-query recall (bm25 relevance vs recency is exactly why order is kept out of the shared
/// contract). Any-token MATCHING is no longer on that list — as of 3.0 every backend splits a query the
/// same way (<see cref="SearchTerms"/>) and only the ranking among matches differs.</summary>
public class MemoryStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly LyntaiOptions _options = new() { MemoryCapPerScope = 3, MemoryRecallLimit = 10 };
    private readonly SqliteMemoryStore _store;

    public MemoryStoreTests() => _store = new SqliteMemoryStore(_db.Factory, _options);

    public void Dispose() => _db.Dispose();

    /// <summary>T5: lock the cross-backend recall guarantee — WHICH entries a query finds, which as of 3.0
    /// is the same everywhere.
    /// <para><b>This test asserted the opposite until 3.0</b>, and its old name said so:
    /// <c>…_and_divergent_for_separated_words</c>. Separated words hit on SQLite (any-token FTS) and missed
    /// on InMemory and Postgres (contiguous substring), and the advice attached to it was "prefer single
    /// salient terms for portable recall" — which is not advice a library should need to give. A shared
    /// tokenization (<see cref="SearchTerms"/>) removed the divergence rather than documenting it.</para>
    /// <para>What is still deliberately NOT asserted is same-match ORDERING: SQLite ranks by bm25, the
    /// others by matched-term count then recency.</para></summary>
    [Fact]
    public async Task Recall_matching_is_consistent_for_single_tokens_and_for_separated_words()
    {
        var opts = new LyntaiOptions();
        var sqlite = new SqliteMemoryStore(_db.Factory, opts);
        IMemoryStore inmem = new InMemoryMemoryStore(opts);
        foreach (var s in new IMemoryStore[] { sqlite, inmem })
            await s.RememberAsync("t", "s", "You can cancel your subscription anytime.");

        // a single ≥3-char token substring recalls on every backend
        Assert.Single(await sqlite.RecallAsync("t", "s", "subscription"));
        Assert.Single(await inmem.RecallAsync("t", "s", "subscription"));

        // and so do words appearing SEPARATELY — "cancel plan" is nowhere contiguous in the content
        Assert.Single(await sqlite.RecallAsync("t", "s", "cancel plan"));
        Assert.Single(await inmem.RecallAsync("t", "s", "cancel plan"));

        // a query sharing no term still misses on both — the convergence is not "match everything"
        Assert.Empty(await sqlite.RecallAsync("t", "s", "refund shipping"));
        Assert.Empty(await inmem.RecallAsync("t", "s", "refund shipping"));
    }

    [Fact]
    public async Task Short_query_falls_back_to_like()
    {
        await _store.RememberAsync("task", "s", "alpha ab beta");
        await _store.RememberAsync("task", "s", "gamma delta");

        var hits = await _store.RecallAsync("task", query: "ab"); // <3 chars → FtsQuery null → LIKE

        Assert.Single(hits);
        Assert.Contains("alpha ab beta", hits[0].Content);
    }

    [Fact]
    public async Task Cap_is_enforced_oldest_trimmed_in_recency_order()
    {
        for (var i = 1; i <= 5; i++)
            await _store.RememberAsync("capped", "s", $"entry {i}");

        var hits = await _store.RecallAsync("capped"); // cap is 3

        Assert.Equal(3, hits.Count);
        Assert.Equal(["entry 5", "entry 4", "entry 3"], hits.Select(h => h.Content)); // newest kept, recency order
    }

    [Fact] // R6: dedup must be enforced by the schema, not just a UPDATE-then-INSERT that two concurrent
           // Remembers could both fall through — a raw duplicate row is rejected by the unique index.
    public async Task Duplicate_fact_is_rejected_by_a_unique_constraint()
    {
        await _store.RememberAsync("t", "s", "the same fact");

        using var conn = _db.Factory.Open();
        var ex = await Assert.ThrowsAsync<SqliteException>(() => conn.ExecuteAsync(
            "INSERT INTO lyntai_memory_entry (task_key, scope, content, created_at) VALUES ('t','s','the same fact','x')"));
        Assert.Contains("UNIQUE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
