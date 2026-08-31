using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>Pins <see cref="LexicalSeedSource"/>'s whole contract: it is a thin pass-through to
/// <see cref="IMemoryGraphStore.SeedAsync"/>, so the source's output must equal a direct store call
/// entry-for-entry and in the SAME order — position is the rank this seam exists to carry.</summary>
public sealed class SeedSourceTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task The_lexical_source_returns_the_stores_own_order_untouched()
    {
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var engine = new GraphMemoryEngine("seedtest", store);

        await engine.RememberAsync(new MemoryWrite("task", "scope", "alpha beta gamma"));
        await engine.RememberAsync(new MemoryWrite("task", "scope", "beta gamma delta"));
        await engine.RememberAsync(new MemoryWrite("task", "scope", "unrelated content"));

        var source = new LexicalSeedSource();
        var request = new MemorySeedRequest("seedtest", store,
            new MemoryQuery("task", Scope: "scope", Query: "beta"), Limit: 10);

        var seeded = await source.SeedAsync(request, CancellationToken.None);
        var direct = await store.SeedAsync("seedtest", "task", "scope", "beta", 10);

        Assert.Equal("lexical", source.Name);
        Assert.Equal(direct.Select(n => n.Id), seeded.Select(n => n.Id));
        Assert.NotEmpty(seeded);
    }
}
