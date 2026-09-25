using Dapper;
using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>SQLite-SPECIFIC memory-store concerns. The cross-backend semantics — matching, the no-query
/// recency sequence, scope/task filtering, dedup, TTL, the cap, forget, fail-open — are pinned by
/// <see cref="MemoryStoreContract"/> (<see cref="SqliteMemoryStoreContractTests"/>). What stays here is
/// SQLite's own: the FTS→LIKE short-query fallback and the schema-enforced dedup.</summary>
public class MemoryStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly LyntaiOptions _options = new() { MemoryEviction = MemoryEvictionPolicy.CountCap(3), MemoryRecallLimit = 10 };
    private readonly SqliteMemoryStore _store;

    public MemoryStoreTests() => _store = new SqliteMemoryStore(_db.Factory, _options);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Short_query_falls_back_to_like()
    {
        await _store.RememberAsync("task", "s", "alpha ab beta");
        await _store.RememberAsync("task", "s", "gamma delta");

        var hits = await _store.RecallAsync("task", query: "ab"); // <3 chars → FtsQuery null → LIKE

        Assert.Single(hits);
        Assert.Contains("alpha ab beta", hits[0].Content);
    }

    [Fact] // Dedup must be enforced by the schema, not just a UPDATE-then-INSERT that two concurrent
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
