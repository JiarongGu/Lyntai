using Lyntai;
using Lyntai.Cortex;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary><c>AddLyntai(b =&gt; b.UsePostgresStorage(conn))</c> resolves every store and round-trips
/// against real Postgres — the full builder path, not just the store classes directly.</summary>
[Collection("postgres")]
public sealed class UsePostgresStorageTests(PostgresFixture pg)
{
    [Fact]
    public async Task Every_store_resolves_and_round_trips()
    {
        if (!pg.Available) return;

        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UsePostgresStorage(pg.ConnectionString)); // idempotent re-migrate + register all stores
        using var sp = services.BuildServiceProvider();

        var suffix = Guid.NewGuid().ToString("N");
        var kv = sp.GetRequiredService<IKeyValueStore>();
        await kv.SetAsync($"k-{suffix}", "v");
        Assert.Equal("v", await kv.GetAsync($"k-{suffix}"));

        var conversations = sp.GetRequiredService<IConversationStore>();
        await conversations.CreateThreadAsync($"t-{suffix}");
        await conversations.AppendMessageAsync($"t-{suffix}", "user", "hi");
        Assert.Single(await conversations.GetMessagesAsync($"t-{suffix}"));

        var memory = sp.GetRequiredService<IMemoryStore>();
        await memory.RememberAsync($"task-{suffix}", "s", "a remembered fact");
        Assert.Single(await memory.RecallAsync($"task-{suffix}", query: "remembered"));

        var scores = sp.GetRequiredService<IScoreStore>();
        await scores.SaveAsync($"s-{suffix}", [new ScoredResult("id", "n", "g", false, 0.5)]);
        Assert.Single(await scores.GetAsync($"s-{suffix}"));

        var traces = sp.GetRequiredService<ITraceStore>();
        await traces.SaveAsync(new RunTrace { SessionId = $"s-{suffix}", Mode = "m", StartedAt = DateTimeOffset.UtcNow });
        Assert.NotNull(await traces.GetAsync($"s-{suffix}"));

        var prompts = sp.GetRequiredService<IPromptVersionStore>();
        await prompts.SaveAsync($"p-{suffix}", "template");
        Assert.NotNull(await prompts.GetActiveAsync($"p-{suffix}"));
    }
}
