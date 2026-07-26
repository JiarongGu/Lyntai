using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>The mastra "one interface per domain, many backends" pattern in a DI-first library: the
/// container IS the registry. Prove a mixed composition — some domains SQLite-backed, one routed to
/// the in-memory backend — resolves each domain to the intended backend without touching consumers.</summary>
public class CompositeStorageTests : IDisposable
{
    private readonly TempDbPath _db = new("composite"); // fresh un-migrated path (UseSqliteStorage migrates)

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Sqlite_for_most_domains_in_memory_for_one()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            b.UseSqliteStorage(_db.Path);   // all domains → SQLite (TryAdd)
            // route memory to the in-memory backend instead — last registration wins in DI
            b.Services.AddSingleton<IMemoryStore>(sp => new InMemoryMemoryStore(sp.GetRequiredService<LyntaiOptions>()));
        });
        using var sp = services.BuildServiceProvider();

        // memory resolves to the in-memory backend...
        Assert.IsType<InMemoryMemoryStore>(sp.GetRequiredService<IMemoryStore>());
        // ...while the rest stay SQLite-backed
        Assert.StartsWith("Sqlite", sp.GetRequiredService<IKeyValueStore>().GetType().Name);
        Assert.StartsWith("Sqlite", sp.GetRequiredService<ITraceStore>().GetType().Name);

        // and each actually works through the composition
        var kv = sp.GetRequiredService<IKeyValueStore>();
        await kv.SetAsync("k", "sqlite-v");
        Assert.Equal("sqlite-v", await kv.GetAsync("k"));

        var memory = sp.GetRequiredService<IMemoryStore>();
        await memory.RememberAsync("t", "s", "in-memory fact");
        Assert.Single(await memory.RecallAsync("t", query: "in-memory"));
    }

    [Fact]
    public async Task Fully_in_memory_stack_resolves_and_round_trips_every_domain()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage());
        using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<IKeyValueStore>().SetAsync("k", "v");
        Assert.Equal("v", await sp.GetRequiredService<IKeyValueStore>().GetAsync("k"));

        await sp.GetRequiredService<IConversationStore>().CreateThreadAsync("t");
        Assert.NotNull(await sp.GetRequiredService<IConversationStore>().GetThreadAsync("t"));

        await sp.GetRequiredService<IMemoryStore>().RememberAsync("task", "s", "a fact");
        Assert.Single(await sp.GetRequiredService<IMemoryStore>().RecallAsync("task"));

        await sp.GetRequiredService<IPromptVersionStore>().SaveAsync("p", "template");
        Assert.NotNull(await sp.GetRequiredService<IPromptVersionStore>().GetActiveAsync("p"));
    }

    [Fact]
    public async Task In_memory_backfills_gaps_after_a_partial_registration()
    {
        // register only KV manually, then UseInMemoryStorage (TryAdd) fills every other domain
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            b.Services.AddSingleton<IKeyValueStore, Lyntai.Storage.InMemory.InMemoryKeyValueStore>();
            b.UseInMemoryStorage(); // TryAdd — must NOT replace the KV above, fills the rest
        });
        using var sp = services.BuildServiceProvider();

        Assert.Single(sp.GetServices<IKeyValueStore>()); // not double-registered
        var memory = sp.GetRequiredService<IMemoryStore>();
        await memory.RememberAsync("t", "s", "backfilled");
        Assert.Single(await memory.RecallAsync("t"));
    }
}
