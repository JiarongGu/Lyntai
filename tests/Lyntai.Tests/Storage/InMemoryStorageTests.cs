using Lyntai;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Storage;

/// <summary>The in-memory backend honors the same domain contracts as SQLite/Postgres — the cross-backend
/// semantics are pinned by the shared *Contract classes (see InMemory*ContractTests). What remains here is
/// the InMemory-SPECIFIC no-query recall SEQUENCE: recency order (contrast SQLite's bm25 relevance), which
/// is exactly the divergence kept OUT of the shared MemoryStoreContract.</summary>
public class InMemoryStorageTests
{
    private DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Memory_cap_trims_oldest_in_recency_order()
    {
        var store = new InMemoryMemoryStore(new LyntaiOptions { MemoryCapPerScope = 3, MemoryRecallLimit = 100 }, clock: () => _now);
        for (var i = 1; i <= 5; i++) await store.RememberAsync("t", "s", $"entry {i}");

        var hits = await store.RecallAsync("t");
        Assert.Equal(3, hits.Count);
        Assert.Equal(["entry 5", "entry 4", "entry 3"], hits.Select(h => h.Content)); // newest kept, recency order
    }
}
