using Lyntai;
using Lyntai.Cortex;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Storage;

/// <summary>The in-memory backend honors the same domain contracts as SQLite — the second backend
/// that proves the storage seam.</summary>
public class InMemoryStorageTests
{
    private DateTimeOffset _now = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task KeyValue_round_trips()
    {
        var kv = new InMemoryKeyValueStore();
        await kv.SetAsync("k", "v");
        Assert.Equal("v", await kv.GetAsync("k"));
        await kv.DeleteAsync("k");
        Assert.Null(await kv.GetAsync("k"));
    }

    [Fact]
    public async Task Conversation_appends_lists_and_cascades()
    {
        var store = new InMemoryConversationStore();
        await store.CreateThreadAsync("t", "title");
        await store.AppendMessageAsync("t", "user", "one");
        await store.AppendMessageAsync("t", "assistant", "two");

        var msgs = await store.GetMessagesAsync("t");
        Assert.Equal(["one", "two"], msgs.Select(m => m.Content));

        await store.DeleteThreadAsync("t");
        Assert.Null(await store.GetThreadAsync("t"));
        Assert.Empty(await store.GetMessagesAsync("t")); // cascade
    }

    [Fact]
    public async Task Conversation_persists_typed_events_and_thread_metadata()
    {
        var store = new InMemoryConversationStore();
        await store.CreateThreadAsync("t", "title", metadata: """{"phase":"plan"}""");
        await store.AppendMessageAsync("t", "phase", """{"phase":"plan"}""");
        await store.AppendMessageAsync("t", "text", "hello");

        var events = await store.GetMessagesAsync("t");
        Assert.Equal(["phase", "text"], events.Select(e => e.Kind));
        Assert.Equal("""{"phase":"plan"}""", (await store.GetThreadAsync("t"))!.Metadata);

        await store.SetThreadMetadataAsync("t", """{"phase":"done"}""");
        Assert.Equal("""{"phase":"done"}""", (await store.GetThreadAsync("t"))!.Metadata);
    }

    [Fact]
    public async Task Memory_dedups_expires_and_recalls_by_substring()
    {
        var store = new InMemoryMemoryStore(new LyntaiOptions { MemoryCapPerScope = 100 }, clock: () => _now);
        await store.RememberAsync("task", "s", "the deploy pipeline is fragile");
        await store.RememberAsync("task", "s", "the deploy pipeline is fragile"); // dedup
        await store.RememberAsync("task", "s", "ephemeral note", ttl: TimeSpan.FromMinutes(5));

        Assert.Equal(2, (await store.RecallAsync("task")).Count); // deduped to 2 live entries

        var hit = await store.RecallAsync("task", query: "PIPELINE"); // case-insensitive substring
        Assert.Single(hit);
        Assert.Contains("pipeline", hit[0].Content);

        _now += TimeSpan.FromMinutes(6);
        Assert.Single(await store.RecallAsync("task")); // ephemeral expired
        Assert.Equal(1, await store.PruneAsync());       // reaped
    }

    [Fact]
    public async Task Memory_cap_trims_oldest()
    {
        var store = new InMemoryMemoryStore(new LyntaiOptions { MemoryCapPerScope = 3, MemoryRecallLimit = 100 }, clock: () => _now);
        for (var i = 1; i <= 5; i++) await store.RememberAsync("t", "s", $"entry {i}");

        var hits = await store.RecallAsync("t");
        Assert.Equal(3, hits.Count);
        Assert.Equal(["entry 5", "entry 4", "entry 3"], hits.Select(h => h.Content));
    }

    [Fact]
    public async Task Score_and_trace_round_trip()
    {
        var scores = new InMemoryScoreStore();
        await scores.SaveAsync("s", [new ScoredResult("id", "n", "g", false, 0.5, "ok")]);
        Assert.Single(await scores.GetAsync("s"));

        var traces = new InMemoryTraceStore();
        await traces.SaveAsync(new RunTrace { SessionId = "s", Mode = "m", StartedAt = _now, TraceId = "abc" });
        var loaded = await traces.GetAsync("s");
        Assert.Equal("abc", loaded!.TraceId);
    }

    [Fact]
    public async Task Prompt_versions_history_and_rollback()
    {
        var store = new InMemoryPromptVersionStore();
        await store.SaveAsync("p", "v1", "alice");
        await store.SaveAsync("p", "v2", "bob");

        Assert.Equal(2, (await store.GetActiveAsync("p"))!.Version);
        Assert.Equal([2, 1], (await store.HistoryAsync("p")).Select(v => v.Version));

        var rolled = await store.RollbackAsync("p", 1);
        Assert.Equal(1, rolled!.Version);
        Assert.Equal("v1", (await store.GetActiveAsync("p"))!.Template);
        Assert.Null(await store.RollbackAsync("p", 99));
    }
}
