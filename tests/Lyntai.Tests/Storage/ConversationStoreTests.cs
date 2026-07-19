using Dapper;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

public class ConversationStoreTests : IDisposable
{
    private readonly TempDb _db = new();
    private readonly SqliteConversationStore _store;

    public ConversationStoreTests() => _store = new SqliteConversationStore(_db.Factory);

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_and_get_thread()
    {
        await _store.CreateThreadAsync("t1", "hello world");

        var thread = await _store.GetThreadAsync("t1");

        Assert.NotNull(thread);
        Assert.Equal("t1", thread.Id);
        Assert.Equal("hello world", thread.Title);
        Assert.True(thread.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Messages_append_and_list_in_order()
    {
        await _store.CreateThreadAsync("t1");
        await _store.AppendMessageAsync("t1", "user", "first");
        await _store.AppendMessageAsync("t1", "assistant", "second");
        await _store.AppendMessageAsync("t1", "user", "第三条消息"); // CJK content must round-trip

        var messages = await _store.GetMessagesAsync("t1");

        Assert.Equal(["first", "second", "第三条消息"], messages.Select(m => m.Content));
        Assert.Equal(["user", "assistant", "user"], messages.Select(m => m.Role));
        Assert.True(messages[0].Id < messages[1].Id);
    }

    [Fact]
    public async Task Delete_thread_cascades_to_messages()
    {
        await _store.CreateThreadAsync("t1");
        await _store.AppendMessageAsync("t1", "user", "doomed");

        await _store.DeleteThreadAsync("t1");

        Assert.Null(await _store.GetThreadAsync("t1"));
        using var conn = _db.Factory.Open();
        Assert.Equal(0L, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM lyntai_message"));
    }

    [Fact]
    public async Task List_threads_newest_first_with_limit()
    {
        await _store.CreateThreadAsync("a");
        await Task.Delay(30);
        await _store.CreateThreadAsync("b");

        var all = await _store.ListThreadsAsync();
        Assert.Equal(["b", "a"], all.Select(t => t.Id));

        var one = await _store.ListThreadsAsync(limit: 1);
        Assert.Equal(["b"], one.Select(t => t.Id));
    }

    // P2 — a conversation is a typed multi-kind event stream, not only role/text chat.
    [Fact]
    public async Task Appends_mixed_kind_events_with_json_payloads_in_seq_order()
    {
        await _store.CreateThreadAsync("t1");
        await _store.AppendMessageAsync("t1", "phase", """{"phase":"plan"}""");
        await _store.AppendMessageAsync("t1", "text", "hello");
        await _store.AppendMessageAsync("t1", "tool", """{"name":"echo","args":{"x":1}}""");

        var events = await _store.GetMessagesAsync("t1");

        Assert.Equal(["phase", "text", "tool"], events.Select(e => e.Kind));
        Assert.Equal(["""{"phase":"plan"}""", "hello", """{"name":"echo","args":{"x":1}}"""],
            events.Select(e => e.Payload));
        Assert.True(events[0].Id < events[1].Id && events[1].Id < events[2].Id); // store-assigned seq
    }

    [Fact]
    public async Task Thread_metadata_round_trips_and_updates()
    {
        await _store.CreateThreadAsync("t1", "title", metadata: """{"phase":"plan"}""");
        Assert.Equal("""{"phase":"plan"}""", (await _store.GetThreadAsync("t1"))!.Metadata);

        await _store.SetThreadMetadataAsync("t1", """{"phase":"done","commit":"abc"}""");
        Assert.Equal("""{"phase":"done","commit":"abc"}""", (await _store.GetThreadAsync("t1"))!.Metadata);

        await _store.CreateThreadAsync("t2"); // metadata is optional
        Assert.Null((await _store.GetThreadAsync("t2"))!.Metadata);
    }

    [Fact]
    public async Task Chat_role_content_aliases_map_to_kind_payload()
    {
        await _store.CreateThreadAsync("t1");
        await _store.AppendMessageAsync("t1", "user", "hi");

        var m = (await _store.GetMessagesAsync("t1"))[0];
        Assert.Equal("user", m.Role);   // Role aliases Kind
        Assert.Equal("hi", m.Content);  // Content aliases Payload
        Assert.Equal(m.Kind, m.Role);
        Assert.Equal(m.Payload, m.Content);
    }
}
