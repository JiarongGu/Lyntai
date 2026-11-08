using Lyntai;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>Adding app-specific ADDITIONAL INFO is a focused extension seam (an <see cref="IConversationEnricher"/>
/// registered into a DI collection), NOT a fork of the store: Lyntai owns the LLM storage; the app's enricher
/// is invoked after a create and an append to persist its own info (in its own store).</summary>
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
            .AddProvider(_ => new FakeTextProvider("p"))
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

    private sealed class ThrowingEnricher(Exception fault) : IConversationEnricher
    {
        public Task OnThreadCreatedAsync(ChatThread t, CancellationToken ct = default) => throw fault;
        public Task OnMessageAppendedAsync(ChatMessage m, CancellationToken ct = default) => throw fault;
    }

    [Fact]
    public async Task A_throwing_enricher_does_not_skip_the_ones_after_it_and_its_fault_is_rethrown()
    {
        var fault = new InvalidOperationException("first enricher");
        var after = new RecordingEnricher();
        var store = new EnrichingConversationStore(new InMemoryConversationStore(),
            [new ThrowingEnricher(fault), after]);

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => store.CreateThreadAsync("t1"));
        Assert.Same(fault, thrown);
        Assert.Equal(["t1"], after.Threads);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.AppendMessageAsync("t1", "user", "hi"));
        Assert.Single(after.Messages);
        Assert.Single(await store.GetMessagesAsync("t1")); // the row is stored, as the interface doc says
    }

    [Fact]
    public async Task Several_failing_enrichers_surface_as_one_AggregateException_after_all_ran()
    {
        var after = new RecordingEnricher();
        var store = new EnrichingConversationStore(new InMemoryConversationStore(),
            [new ThrowingEnricher(new InvalidOperationException("a")), new ThrowingEnricher(new ArgumentException("b")), after]);

        var aggregate = await Assert.ThrowsAsync<AggregateException>(() => store.CreateThreadAsync("t1"));

        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Equal(["t1"], after.Threads);
    }

    [Fact]
    public async Task The_callers_cancellation_still_propagates_at_once()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var after = new RecordingEnricher();
        var store = new EnrichingConversationStore(new InMemoryConversationStore(),
            [new ThrowingEnricher(new OperationCanceledException(cts.Token)), after]);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CreateThreadAsync("t1", ct: cts.Token));
    }

    [Fact]
    public void No_enricher_registered_resolves_the_plain_backend_store()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b.AddProvider(_ => new FakeTextProvider("p")).UseInMemoryStorage());
        using var sp = services.BuildServiceProvider();

        Assert.IsType<InMemoryConversationStore>(sp.GetRequiredService<IConversationStore>()); // not wrapped
    }

    [Fact] // Wrapping must PRESERVE the BYO store's lifetime, not silently promote it to singleton
    public void Enrichment_wrap_preserves_a_byo_stores_registered_lifetime()
    {
        var services = new ServiceCollection();
        services.AddScoped<IConversationStore, InMemoryConversationStore>(); // BYO, deliberately scoped
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .AddConversationEnricher(_ => new RecordingEnricher()));

        var descriptor = services.Last(d => d.ServiceType == typeof(IConversationStore));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime); // wrapped, but still scoped

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        Assert.IsType<EnrichingConversationStore>(scope.ServiceProvider.GetRequiredService<IConversationStore>());
    }
}
