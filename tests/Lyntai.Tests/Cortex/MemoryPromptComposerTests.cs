using Lyntai.Cortex;
using Lyntai.Storage;

namespace Lyntai.Tests.Cortex;

public class MemoryPromptComposerTests
{
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
        public Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null, string? query = null,
            int? limit = null, CancellationToken ct = default) => Task.FromResult(entries);

        public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class ThrowingMemoryStore : IMemoryStore
    {
        public Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null, string? query = null,
            int? limit = null, CancellationToken ct = default) => throw new InvalidOperationException("store down");

        public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
