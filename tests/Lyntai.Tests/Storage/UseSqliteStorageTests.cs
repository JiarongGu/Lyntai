using Lyntai;
using Lyntai.Cortex;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

public class UseSqliteStorageTests : IDisposable
{
    private readonly TempDbPath _db = new("use-sqlite"); // fresh un-migrated path (UseSqliteStorage migrates)

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Every_store_resolves_and_round_trips()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("fake"))
            .UseSqliteStorage(_db.Path));
        using var sp = services.BuildServiceProvider();

        var kv = sp.GetRequiredService<IKeyValueStore>();
        await kv.SetAsync("k", "v");
        Assert.Equal("v", await kv.GetAsync("k"));

        var conversations = sp.GetRequiredService<IConversationStore>();
        await conversations.CreateThreadAsync("t");
        await conversations.AppendMessageAsync("t", "user", "hi");
        Assert.Single(await conversations.GetMessagesAsync("t"));

        var memory = sp.GetRequiredService<IMemoryStore>();
        await memory.RememberAsync("task", "scope", "a remembered fact");
        Assert.Single(await memory.RecallAsync("task", query: "remembered"));

        var scores = sp.GetRequiredService<IScoreStore>();
        await scores.SaveAsync("s", [new ScoredResult("id", "n", "g", false, 0.5)]);
        Assert.Single(await scores.GetAsync("s"));

        var traces = sp.GetRequiredService<ITraceStore>();
        await traces.SaveAsync(new RunTrace { SessionId = "s", Mode = "m", StartedAt = DateTimeOffset.UtcNow });
        Assert.NotNull(await traces.GetAsync("s"));
    }
}
