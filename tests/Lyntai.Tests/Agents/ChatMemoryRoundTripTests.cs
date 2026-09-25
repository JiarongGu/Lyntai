using Lyntai.Agents;
using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Agents;

/// <summary>A chat must recall what it said earlier. The composer READS through one engine, so the chat has to
/// WRITE through that same engine — two turns, and the second turn's composed prompt carries the first.</summary>
public class ChatMemoryRoundTripTests
{
    private const string Answer = "the build gate is dev.mjs verify";

    private static async Task<string> SecondTurnPrompt(Action<LyntaiBuilder> storage)
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse(Answer, ProviderVerdict.Ok));
        provider.Replies.Enqueue(new TextResponse("noted", ProviderVerdict.Ok));

        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => provider).UseDefaultCandidates("p");
            storage(b);
        });
        await using var sp = services.BuildServiceProvider();
        var chat = sp.GetRequiredService<IChatOrchestrator>();

        await chat.ChatAsync(new ChatTurn { Message = "what is the build gate?", TaskKey = "t1", UseTools = false });
        await chat.ChatAsync(new ChatTurn { Message = "remind me of the build gate", TaskKey = "t1", UseTools = false });

        Assert.Equal(2, provider.Calls.Count);
        return provider.Calls[1].Messages[^1].Content;
    }

    [Fact]
    public async Task The_README_headline_chat_over_SQLite_recalls_its_own_earlier_turn()
    {
        using var db = new TempDbPath("chat-memory");

        var prompt = await SecondTurnPrompt(b => b.UseSqliteStorage(db.Path).AddMemory());

        Assert.Contains(Answer, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_in_process_AddMemory_chat_recalls_its_own_earlier_turn()
    {
        var prompt = await SecondTurnPrompt(b => b.UseInMemoryStorage().AddMemory());

        Assert.Contains(Answer, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_chat_with_no_engine_registered_still_recalls_its_own_earlier_turn()
    {
        var prompt = await SecondTurnPrompt(b => b.UseInMemoryStorage());

        Assert.Contains(Answer, prompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_engine_UseMemoryComposer_names_is_the_one_the_chat_writes_into()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse(Answer, ProviderVerdict.Ok));
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => provider).UseDefaultCandidates("p")
            .UseInMemoryStorage()
            .AddMemoryEngine("chat", e => e.UseGraph())
            .UseMemoryComposer("chat"));
        await using var sp = services.BuildServiceProvider();

        await sp.GetRequiredService<IChatOrchestrator>()
            .ChatAsync(new ChatTurn { Message = "what is the build gate?", TaskKey = "t1", UseTools = false });

        var recall = await sp.GetRequiredService<IMemoryEngineFactory>().Get("chat")
            .RecallAsync(new MemoryQuery("t1", "chat", "build gate", Detail: MemoryDetail.Full));
        Assert.Contains(recall.Items, i => (i.Content ?? i.Headline).Contains(Answer, StringComparison.Ordinal));
    }

    // ---- the write is fail-open, but never on the CALLER's cancellation ------------------------------------

    /// <summary>A keyword store whose write either cancels the CALLER's token mid-call (the real cancel) or
    /// throws its OWN deadline with the caller's token untouched. The exception is MARKED, so nothing else's
    /// cancellation can satisfy the assertion.</summary>
    private sealed class CancellingMemoryStore(CancellationTokenSource? caller) : IMemoryStore
    {
        public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null,
            CancellationToken ct = default)
        {
            if (caller is null) throw new TaskCanceledException("the store's own deadline");
            caller.Cancel();
            throw new OperationCanceledException("the caller left mid-write", ct);
        }

        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
            string? query = null, int? limit = null, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<MemoryEntry>>([]);

        public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null,
            CancellationToken ct = default) => Task.FromResult(0);
    }

    private static ServiceProvider WithStore(IMemoryStore store, FakeTextProvider provider)
    {
        var services = new ServiceCollection();
        services.AddSingleton(store);
        services.AddLyntai(b => b.AddProvider(_ => provider).UseDefaultCandidates("p"));
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task The_callers_cancellation_during_the_memory_write_propagates()
    {
        using var cts = new CancellationTokenSource();
        var provider = new FakeTextProvider("p");
        await using var sp = WithStore(new CancellingMemoryStore(cts), provider);

        var ex = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            sp.GetRequiredService<IChatOrchestrator>().ChatAsync(
                new ChatTurn { Message = "hello", TaskKey = "t1", UseTools = false }, cts.Token));

        Assert.Equal("the caller left mid-write", ex.Message);
    }

    [Fact]
    public async Task A_stores_OWN_deadline_during_the_memory_write_leaves_the_turn_Ok()
    {
        var provider = new FakeTextProvider("p");
        provider.Replies.Enqueue(new TextResponse(Answer, ProviderVerdict.Ok));
        await using var sp = WithStore(new CancellingMemoryStore(caller: null), provider);

        var result = await sp.GetRequiredService<IChatOrchestrator>().ChatAsync(
            new ChatTurn { Message = "hello", TaskKey = "t1", UseTools = false });

        Assert.True(result.Ok);
        Assert.Equal(Answer, result.Answer);
    }
}
