using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Core;

/// <summary>Adding app-specific ADDITIONAL INFO is a focused extension seam (an <see cref="IConversationEnricher"/>
/// registered into a DI collection), NOT a fork of the store: Lyntai owns the LLM storage; the app's enricher
/// is invoked after each write to persist its own info (in its own store).</summary>
public class ConversationEnricherTests
{
    private sealed class RecordingEnricher : IConversationEnricher
    {
        public List<string> Threads { get; } = [];
        public List<(string Id, long Seq, string Kind)> Messages { get; } = [];
        public Task OnThreadCreatedAsync(ChatThread t, CancellationToken ct = default) { Threads.Add(t.Id); return Task.CompletedTask; }
        public Task OnMessageAppendedAsync(ChatMessage m, CancellationToken ct = default) { Messages.Add((m.Id, m.Seq, m.Kind)); return Task.CompletedTask; }
    }

    [Fact]
    public async Task Enrichers_fire_after_writes_without_replacing_the_store()
    {
        var rec = new RecordingEnricher();
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddConversationEnricher(_ => rec));
        using var sp = services.BuildServiceProvider();

        var store = sp.GetRequiredService<IConversationStore>();
        await store.CreateThreadAsync("t1", "hi");
        var m = await store.AppendMessageAsync("t1", "user", "hello");

        // the core store still works (Lyntai owns it) ...
        Assert.Equal(["hello"], (await store.GetMessagesAsync("t1")).Select(x => x.Payload));
        // ... and the enricher saw each write with the persisted record (store-assigned Id/Seq)
        Assert.Equal(["t1"], rec.Threads);
        Assert.Equal([(m.Id, 1L, "user")], rec.Messages);
    }

    [Fact]
    public void No_enricher_registered_resolves_the_plain_backend_store()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p")).UseInMemoryStorage());
        using var sp = services.BuildServiceProvider();

        Assert.IsType<InMemoryConversationStore>(sp.GetRequiredService<IConversationStore>()); // not wrapped
    }
}
