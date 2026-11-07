using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>The SQLite memory store past the engine's bound-variable limit: a scope evicted or recalled in
/// bulk must not expand one parameter per id, which fails (the eviction) or is swallowed (the access touch)
/// once the list outgrows the limit.</summary>
public class SqliteMemoryLargeScopeTests : IDisposable
{
    // above the 32766 a SQLite build of 3.32 or later allows by default
    private const int Rows = 33_000;

    private readonly TempDb _db = new();
    private readonly DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    public void Dispose() => _db.Dispose();

    private async Task SeedAsync(DateTimeOffset lastAccessed)
    {
        await using var conn = await _db.Factory.OpenAsync();
        await conn.ExecuteAsync(new CommandDefinition("""
            WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < @rows)
            INSERT INTO lyntai_memory_entry (task_key, scope, content, created_at, last_accessed_at)
            SELECT 't', 's', 'bulk entry ' || i, @created, @lastAccessed FROM n
            """, new { rows = Rows, created = _now, lastAccessed }));
    }

    private SqliteMemoryStore Store(MemoryEvictionPolicy policy) =>
        new(_db.Factory, new LyntaiOptions { MemoryEviction = policy, MemoryRecallLimit = Rows + 10 }, clock: () => _now);

    [Fact]
    public async Task A_size_budget_eviction_of_a_scope_past_the_variable_limit_completes()
    {
        await SeedAsync(_now);
        var store = Store(MemoryEvictionPolicy.SizeBudget(200));

        await store.RememberAsync("t", "s", "one more entry");

        var left = await store.RecallAsync("t", "s", limit: Rows + 10);
        Assert.InRange(left.Count, 1, 40);                          // the budget held, nothing threw
        Assert.Contains(left, e => e.Content == "one more entry");  // and the newest survived
    }

    [Fact]
    public async Task A_recall_past_the_variable_limit_refreshes_every_entry_it_returns()
    {
        var stale = _now - TimeSpan.FromDays(1);
        await SeedAsync(stale);
        var store = Store(MemoryEvictionPolicy.CountCap(Rows + 100, MemoryEvictionMode.Lru));

        var hits = await store.RecallAsync("t", "s", query: "bulk entry", limit: Rows + 10);

        Assert.True(hits.Count > 32_766, $"the recall returned {hits.Count}");
        await using var conn = await _db.Factory.OpenAsync();
        var refreshed = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COUNT(*) FROM lyntai_memory_entry WHERE last_accessed_at = @now", new { now = _now }));
        Assert.Equal(hits.Count, refreshed);
    }
}
