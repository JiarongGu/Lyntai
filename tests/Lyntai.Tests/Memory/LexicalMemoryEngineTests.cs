using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage;

namespace Lyntai.Tests.Memory;

public class LexicalMemoryEngineTests
{
    private static LexicalMemoryEngine Engine(out FakeMemoryStore store)
    {
        store = new FakeMemoryStore();
        return new LexicalMemoryEngine("chat/lexical", store);
    }

    [Fact]
    public async Task Remember_then_recall_round_trips_through_the_store()
    {
        var engine = Engine(out _);
        await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "pipeline"));

        Assert.Single(recall.Items);
        Assert.Equal("the deploy pipeline needs approval", recall.Items[0].Headline);
        Assert.Equal(MemorySources.Lexical, recall.Ran);
    }

    [Fact]
    public async Task The_reference_is_stable_across_write_and_recall()
    {
        var engine = Engine(out _);
        var written = await engine.RememberAsync(new MemoryWrite("t", "s", "stable identity"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "stable"));

        Assert.Equal(written, recall.Items[0].Reference);
        Assert.Equal("chat/lexical", written.Engine);
    }

    [Fact]
    public void It_reports_that_it_can_only_store_associative_material()
    {
        var engine = Engine(out _);
        Assert.Equal(MemoryGrades.Associative, engine.Supported);
    }

    [Fact]
    public async Task An_authoritative_write_throws_rather_than_being_downgraded()
    {
        var engine = Engine(out var store);

        var ex = await Assert.ThrowsAsync<NotSupportedException>(() =>
            engine.RememberAsync(new MemoryWrite("t", "s", "exact fact", Grade: MemoryGrade.Authoritative)));

        Assert.Contains("chat/lexical", ex.Message, StringComparison.Ordinal);
        Assert.Empty(await store.RecallAsync("t")); // nothing was written
    }

    [Fact]
    public async Task A_storage_fault_during_recall_degrades_to_empty_rather_than_throwing()
    {
        var engine = new LexicalMemoryEngine("chat/lexical", new ThrowingStore());

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "anything"));

        Assert.Empty(recall.Items);
        Assert.Equal(MemorySources.None, recall.Ran);
    }

    private sealed class ThrowingStore : IMemoryStore
    {
        public Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null,
            CancellationToken ct = default) => throw new InvalidOperationException("boom");

        public Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
            string? query = null, int? limit = null, CancellationToken ct = default) =>
            throw new InvalidOperationException("boom");

        public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null,
            CancellationToken ct = default) => Task.FromResult(0);
    }
}
