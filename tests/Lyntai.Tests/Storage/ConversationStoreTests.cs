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
        Assert.Equal(0L, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM message"));
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
}
