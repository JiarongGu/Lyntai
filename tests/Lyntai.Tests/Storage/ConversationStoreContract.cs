using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>Backend-agnostic <see cref="IConversationStore"/> contract — run by the InMemory, SQLite, and
/// Postgres test classes so thread/metadata/event-stream/seq/cascade semantics are pinned identically.
/// Thread ids are namespaced by a caller-supplied <paramref name="key"/> so the methods are safe on the
/// shared Postgres container (InMemory/SQLite get a fresh store per test).</summary>
public static class ConversationStoreContract
{
    public static async Task Create_and_get_thread(IConversationStore store, string key)
    {
        var t = key + "-t1";
        await store.CreateThreadAsync(t, "hello world");

        var thread = await store.GetThreadAsync(t);
        Assert.NotNull(thread);
        Assert.Equal(t, thread!.Id);
        Assert.Equal("hello world", thread.Title);
        Assert.True(thread.CreatedAt > DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Null(await store.GetThreadAsync(key + "-missing")); // unknown → null
    }

    /// <summary>S6 — a duplicate thread id THROWS on every backend (the SQL backends' PK violation);
    /// InMemory used to silently overwrite while keeping the old thread's messages — the classic
    /// test-on-InMemory / deploy-on-SQL divergence.</summary>
    public static async Task Duplicate_thread_id_throws_and_preserves_the_original(IConversationStore store, string key)
    {
        var t = key + "-dup";
        await store.CreateThreadAsync(t, "first");

        await Assert.ThrowsAnyAsync<Exception>(() => store.CreateThreadAsync(t, "second"));
        Assert.Equal("first", (await store.GetThreadAsync(t))!.Title); // the original survives untouched
    }

    public static async Task Thread_metadata_round_trips_and_updates(IConversationStore store, string key)
    {
        var t = key + "-meta";
        await store.CreateThreadAsync(t, "title", metadata: """{"phase":"plan"}""");
        Assert.Equal("""{"phase":"plan"}""", (await store.GetThreadAsync(t))!.Metadata);

        await store.SetThreadMetadataAsync(t, """{"phase":"done","commit":"abc"}""");
        Assert.Equal("""{"phase":"done","commit":"abc"}""", (await store.GetThreadAsync(t))!.Metadata);

        var t2 = key + "-nometa";
        await store.CreateThreadAsync(t2); // metadata is optional
        Assert.Null((await store.GetThreadAsync(t2))!.Metadata);
    }

    public static async Task Appends_mixed_kind_events_with_json_payloads_in_seq_order(IConversationStore store, string key)
    {
        var t = key + "-events";
        await store.CreateThreadAsync(t);
        await store.AppendMessageAsync(t, "phase", """{"phase":"plan"}""");
        await store.AppendMessageAsync(t, "text", "hello");
        await store.AppendMessageAsync(t, "tool", """{"name":"echo","args":{"x":1}}""");

        var events = await store.GetMessagesAsync(t);
        Assert.Equal(["phase", "text", "tool"], events.Select(e => e.Kind));
        Assert.Equal(["""{"phase":"plan"}""", "hello", """{"name":"echo","args":{"x":1}}"""],
            events.Select(e => e.Payload));
        Assert.Equal([1L, 2L, 3L], events.Select(e => e.Seq)); // per-thread store-assigned seq
    }

    public static async Task Cjk_payload_round_trips(IConversationStore store, string key)
    {
        var t = key + "-cjk";
        await store.CreateThreadAsync(t);
        await store.AppendMessageAsync(t, "user", "first");
        await store.AppendMessageAsync(t, "assistant", "second");
        await store.AppendMessageAsync(t, "user", "第三条消息"); // CJK content must round-trip

        var msgs = await store.GetMessagesAsync(t);
        Assert.Equal(["first", "second", "第三条消息"], msgs.Select(m => m.Content));
        Assert.Equal(["user", "assistant", "user"], msgs.Select(m => m.Role));
        Assert.Equal([1L, 2L, 3L], msgs.Select(m => m.Seq));
    }

    public static async Task Seq_is_1_based_and_restarts_per_thread_with_guid_ids_and_per_message_metadata(IConversationStore store, string key)
    {
        var a = key + "-a";
        var b = key + "-b";
        await store.CreateThreadAsync(a);
        await store.CreateThreadAsync(b);
        var a1 = await store.AppendMessageAsync(a, "text", "a-one");
        var b1 = await store.AppendMessageAsync(b, "text", "b-one", metadata: """{"tokens":5}""");
        var a2 = await store.AppendMessageAsync(a, "text", "a-two");

        Assert.Equal(1L, a1.Seq);
        Assert.Equal(1L, b1.Seq); // per-thread: b restarts at 1
        Assert.Equal(2L, a2.Seq);
        Assert.True(Guid.TryParse(a1.Id, out _));                          // Id is a GUID handle
        Assert.Equal(3, new[] { a1.Id, b1.Id, a2.Id }.Distinct().Count()); // globally unique

        var bMsgs = await store.GetMessagesAsync(b);
        Assert.Equal("""{"tokens":5}""", bMsgs[0].Metadata);            // per-message metadata round-trips
        Assert.Null((await store.GetMessagesAsync(a))[0].Metadata);     // metadata is optional
    }

    public static async Task Role_content_aliases_map_to_kind_payload(IConversationStore store, string key)
    {
        var t = key + "-alias";
        await store.CreateThreadAsync(t);
        await store.AppendMessageAsync(t, "user", "hi");

        var m = (await store.GetMessagesAsync(t))[0];
        Assert.Equal("user", m.Role);  // Role aliases Kind
        Assert.Equal("hi", m.Content);  // Content aliases Payload
        Assert.Equal(m.Kind, m.Role);
        Assert.Equal(m.Payload, m.Content);
    }

    public static async Task Delete_thread_cascades_to_messages(IConversationStore store, string key)
    {
        var t = key + "-doomed";
        await store.CreateThreadAsync(t);
        await store.AppendMessageAsync(t, "user", "doomed");

        await store.DeleteThreadAsync(t);
        Assert.Null(await store.GetThreadAsync(t));
        Assert.Empty(await store.GetMessagesAsync(t)); // cascade
    }

    public static async Task List_threads_returns_newest_first(IConversationStore store, string key)
    {
        // Only assert the RELATIVE order of the two threads THIS test created (the shared Postgres
        // container may hold other threads); all three backends order by created_at DESC, id DESC.
        var older = key + "-older";
        var newer = key + "-newer";
        await store.CreateThreadAsync(older);
        await Task.Delay(30); // ensure a distinct created_at tick
        await store.CreateThreadAsync(newer);

        var mine = (await store.ListThreadsAsync(limit: 1000)).Where(t => t.Id == older || t.Id == newer).ToList();
        Assert.Equal([newer, older], mine.Select(t => t.Id)); // newest first
    }

    public static async Task Count_reflects_inserted_and_deleted_threads(IConversationStore store, string key)
    {
        // The shared Postgres container may hold other threads, so assert on the DELTA, not an absolute.
        var before = await store.CountThreadsAsync();
        await store.CreateThreadAsync(key + "-c1");
        await store.CreateThreadAsync(key + "-c2");
        await store.CreateThreadAsync(key + "-c3");
        Assert.Equal(before + 3, await store.CountThreadsAsync());

        await store.DeleteThreadAsync(key + "-c2");
        Assert.Equal(before + 2, await store.CountThreadsAsync());
    }

    public static async Task Paged_cursor_walks_every_thread_exactly_once(IConversationStore store, string key)
    {
        // Rapid inserts (no delay) deliberately let several threads share a created_at tick so the keyset
        // cursor's id tiebreak is exercised — a naive created_at-only cursor would skip or duplicate them.
        var ids = Enumerable.Range(0, 5).Select(i => $"{key}-p{i}").ToList();
        foreach (var id in ids) await store.CreateThreadAsync(id);

        var mine = new HashSet<string>(ids);
        var collected = new List<string>();
        ChatThread? cursor = null;
        for (var page = 0; page < 500 && collected.Count(mine.Contains) < ids.Count; page++)
        {
            var batch = await store.ListThreadsPageAsync(limit: 2, after: cursor);
            Assert.True(batch.Count <= 2);       // a page never exceeds its limit (not list-all-then-filter)
            if (batch.Count == 0) break;         // walked off the end
            collected.AddRange(batch.Select(t => t.Id));
            cursor = batch[^1];
        }

        var found = collected.Where(mine.Contains).ToList();
        Assert.Equal(ids.Count, found.Count);              // every inserted thread surfaced
        Assert.Equal(ids.Count, found.Distinct().Count()); // exactly once — no cursor overlap/skip
    }

    /// <summary>The keyset cursor's id tiebreak, exercised DETERMINISTICALLY.
    ///
    /// <para><see cref="Paged_cursor_walks_every_thread_exactly_once"/> inserts without a delay and its
    /// comment says that "deliberately" lets threads share a <c>created_at</c> tick — but nothing makes them.
    /// The timestamp comes from the store's own <c>UtcNow</c> and there is no injectable clock, so on a
    /// fine-resolution clock every row gets a distinct tick, the <c>id</c> branch never executes, and the
    /// test passes anyway. That is a test which cannot fail for the reason it exists.</para>
    ///
    /// <para>This one needs no two rows to collide: it makes the CURSOR share a timestamp with a real row,
    /// which is entirely under the test's control because <see cref="ChatThread"/> is a public record. A
    /// cursor at the same instant with a HIGHER id must still yield the row; the same cursor with a LOWER id
    /// must not. Those are the two sides of <c>created_at = @after AND id &lt; @afterId</c>, and a
    /// <c>created_at</c>-only cursor fails both.</para>
    ///
    /// <para>Ids differ only in a DIGIT on purpose. The comparison is ordinal in process and a collated
    /// string comparison in SQL, and those disagree about letter case on some collations — a real question,
    /// but a different one from whether the branch works. Digits order identically under every collation
    /// these backends ship with, so a failure here is the tiebreak and never the collation.</para></summary>
    public static async Task A_cursor_at_the_same_instant_falls_back_to_the_id(IConversationStore store, string key)
    {
        var created = await store.CreateThreadAsync($"{key}-5");

        // same instant, HIGHER id — the row sorts after the cursor on the tiebreak, so it must come back
        var lower = await store.ListThreadsPageAsync(limit: 50,
            after: new ChatThread($"{key}-9", null, created.CreatedAt));
        Assert.Contains($"{key}-5", lower.Select(t => t.Id));

        // same instant, LOWER id — the row sorts at or before the cursor, so it must NOT come back
        var higher = await store.ListThreadsPageAsync(limit: 50,
            after: new ChatThread($"{key}-1", null, created.CreatedAt));
        Assert.DoesNotContain($"{key}-5", higher.Select(t => t.Id));

        // and the row is never its OWN successor — the boundary a `<=` would get wrong
        var itself = await store.ListThreadsPageAsync(limit: 50, after: created);
        Assert.DoesNotContain($"{key}-5", itself.Select(t => t.Id));
    }
}
