using Lyntai.Cortex;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

public class MemoryPromptComposerTests
{
    // ---- hybrid recall (lexical + semantic) ----------------------------------------------------------

    private static SemanticMemory SemanticWith(params string[] facts)
    {
        var mem = new SemanticMemory([new FakeEmbedder()], new InMemoryVectorStore());
        foreach (var f in facts) mem.RememberAsync("trip", "s", f).GetAwaiter().GetResult();
        return mem;
    }

    // ---- "Never throws" has to survive a BYO store's OWN deadline -------------------------------------
    // The type doc promises "an outage in either source yields whatever the other returned (or the base
    // prompt)". A custom store that imposes its own timeout raises the SAME exception type the caller's
    // cancel does, so a bare rethrow made that promise false for exactly the implementations it was
    // written to protect.

    private sealed class TimingOutMemoryStore : IMemoryStore
    {
        public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null,
            CancellationToken ct = default) => throw new OperationCanceledException("the store's own deadline");
        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
            string? query = null, int? limit = null, CancellationToken ct = default) =>
            throw new OperationCanceledException("the store's own deadline");
        public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
            throw new OperationCanceledException("the store's own deadline");
        public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null,
            CancellationToken ct = default) => throw new OperationCanceledException("the store's own deadline");
    }

    [Fact]
    public async Task A_lexical_stores_OWN_timeout_leaves_the_semantic_half_intact()
    {
        var composer = new MemoryPromptComposer(new TimingOutMemoryStore(), SemanticWith("embed me"));

        var composed = await composer.ComposeAsync("base", "trip", scope: "s", query: "embed me");

        Assert.Contains("- embed me", composed);   // the other source still answered
    }

    [Fact]
    public async Task A_lexical_stores_OWN_timeout_with_no_other_source_yields_the_BASE_prompt()
    {
        var composer = new MemoryPromptComposer(new TimingOutMemoryStore());

        var composed = await composer.ComposeAsync("base", "trip", scope: "s", query: "anything");

        Assert.Equal("base", composed);
    }

    [Fact]
    public async Task The_CALLERS_cancellation_still_propagates_out_of_compose()
    {
        // The control: without it, "swallow every OperationCanceledException" passes both tests above.
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var composer = new MemoryPromptComposer(new TimingOutMemoryStore());

        await Assert.ThrowsAsync<OperationCanceledException>(
            async () => await composer.ComposeAsync("base", "trip", scope: "s", query: "q", ct: cts.Token));
    }

    [Fact]
    public async Task Hybrid_leads_with_semantic_hits_and_dedups_against_lexical()
    {
        var semantic = SemanticWith("cancel anytime");
        var store = new FakeMemoryStore([Fact("cancel anytime"), Fact("lexical only fact")]);
        var composer = new MemoryPromptComposer(store, semantic);

        var composed = await composer.ComposeAsync("base", "trip", scope: "s", query: "how do I cancel");

        Assert.Contains("- cancel anytime", composed);
        Assert.Contains("- lexical only fact", composed);
        // deduped: the shared fact appears once even though both sources returned it
        Assert.Equal(composed.IndexOf("cancel anytime"), composed.LastIndexOf("cancel anytime"));
        // semantic-first: the query-relevant hit precedes the lexical-only fact
        Assert.True(composed.IndexOf("cancel anytime") < composed.IndexOf("lexical only fact"));
    }

    [Fact]
    public async Task Semantic_only_composer_appends_recalled_hits()
    {
        var composer = new MemoryPromptComposer(memory: null, semantic: SemanticWith("embed me"));
        var composed = await composer.ComposeAsync("base", "trip", scope: "s", query: "embed me");
        Assert.Contains("- embed me", composed);
    }

    [Fact]
    public async Task A_throwing_semantic_source_falls_through_to_lexical()
    {
        var store = new FakeMemoryStore([Fact("lexical fact")]);
        var composer = new MemoryPromptComposer(store, new ThrowingSemanticMemory());
        var composed = await composer.ComposeAsync("base", "trip", scope: "s", query: "q");
        Assert.Contains("- lexical fact", composed); // semantic failure didn't sink the lexical facts
    }

    private sealed class ThrowingSemanticMemory : ISemanticMemory
    {
        public Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<SemanticHit>> RecallAsync(string taskKey, string? scope, string query,
            int k = 5, double minScore = 0, CancellationToken ct = default) => throw new InvalidOperationException("embedder down");
        public Task ForgetAsync(string taskKey, string scope, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public async Task No_store_returns_the_base_prompt_unchanged()
    {
        var composer = new MemoryPromptComposer(memory: null);
        Assert.Equal("base", await composer.ComposeAsync("base", "task"));
    }

    [Fact]
    public async Task Recalled_facts_are_appended()
    {
        var store = new FakeMemoryStore([Fact("fact one"), Fact("fact two")]);
        var composer = new MemoryPromptComposer(store);

        var composed = await composer.ComposeAsync("base prompt", "trip");

        Assert.Contains("base prompt", composed);
        Assert.Contains("## Learned facts (trip)", composed);
        Assert.Contains("- fact one", composed);
        Assert.Contains("- fact two", composed);
    }

    [Fact]
    public async Task The_appended_section_is_bounded_by_a_character_budget()
    {
        // a handful of multi-KB facts must not blow the context window (design §8: max entries / char cap)
        var big = new string('x', 3000);
        var store = new FakeMemoryStore([Fact(big), Fact(big), Fact(big)]);
        var composer = new MemoryPromptComposer(store, maxChars: 4000);

        var composed = await composer.ComposeAsync("base", "trip");

        // base + header + at most the char budget of facts — never all 9000 chars of them
        Assert.True(composed.Length < 4000 + 100, $"composed was {composed.Length} chars");
        Assert.Contains(big, composed); // at least one fact still made it in
    }

    [Fact]
    public async Task A_throwing_store_fails_open_to_the_base_prompt()
    {
        var composer = new MemoryPromptComposer(new ThrowingMemoryStore());
        Assert.Equal("base", await composer.ComposeAsync("base", "task"));
    }

    private static MemoryEntry Fact(string content) =>
        new(1, "trip", "", content, DateTimeOffset.UnixEpoch);

    private sealed class FakeMemoryStore(IReadOnlyList<MemoryEntry> entries) : IMemoryStore
    {
        public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null, string? query = null,
            int? limit = null, CancellationToken ct = default) => Task.FromResult(entries);

        public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default) =>
            Task.FromResult(0);
    }

    private sealed class ThrowingMemoryStore : IMemoryStore
    {
        public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null, string? query = null,
            int? limit = null, CancellationToken ct = default) => throw new InvalidOperationException("store down");

        public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default) =>
            Task.FromResult(0);
    }
}
