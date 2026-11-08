using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary><see cref="ChatThreads.Page"/> orders and cuts a same-instant tie by the id's UTF-8 bytes, as SQLite's
/// BINARY collation does, so a code point past U+FFFF comes after U+E000 to U+FFFF rather than before them.</summary>
public class ChatThreadsPageTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);
    private const string Astral = "t-\U0001F600";
    private const string Private = "t-";

    private static ChatThread Thread(string id) => new(id, null, At);

    [Fact]
    public void A_same_instant_tie_is_broken_by_the_ids_utf8_bytes()
    {
        var page = ChatThreads.Page([Thread(Private), Thread(Astral), Thread("t-")], limit: 10, after: null);

        // newest first, and within one instant the id DESCENDING by bytes: U+1F600 > U+E000 > the shorter id
        Assert.Equal([Astral, Private, "t-"], page.Select(t => t.Id));
    }

    [Fact]
    public void The_cursor_compares_ids_the_same_way()
    {
        var all = new[] { Thread(Private), Thread(Astral), Thread("t-") };

        Assert.Equal([Private, "t-"], ChatThreads.Page(all, 10, after: Thread(Astral)).Select(t => t.Id));
        Assert.Equal(["t-"], ChatThreads.Page(all, 10, after: Thread(Private)).Select(t => t.Id));
        Assert.Empty(ChatThreads.Page(all, 10, after: Thread("t-")));
    }
}
