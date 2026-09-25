namespace Lyntai.Storage;

/// <summary>A conversation thread. (Named ChatThread — plain "Thread" collides with System.Threading.)
/// <paramref name="Metadata"/> is optional opaque thread-level state (a small JSON blob the app owns —
/// e.g. a phase/plan projection) so an app needn't bolt on a bespoke per-thread column.</summary>
public sealed record ChatThread(string Id, string? Title, DateTimeOffset CreatedAt, string? Metadata = null);

/// <summary>One event in a thread's stream. A conversation is, in general, a typed multi-kind event stream
/// (text / tool-call / tool-result / usage / thinking / phase / error), not only role/text chat turns.
/// <list type="bullet">
/// <item><paramref name="Id"/> — a globally-unique GUID handle (store-generated).</item>
/// <item><paramref name="Seq"/> — the 1-based sequence WITHIN the thread; events order by this (external
/// event-stream schemas key on <c>(thread_id, seq)</c>).</item>
/// <item><paramref name="Kind"/> — the event type / message type (chat uses a role: user/assistant/
/// system/tool; richer streams use text/tool-call/tool-result/usage/thinking/phase/error).</item>
/// <item><paramref name="Payload"/> — the event body (chat uses the message text; richer events carry JSON).</item>
/// <item><paramref name="Metadata"/> — optional per-event structured metadata (JSON — usage, model, …).</item>
/// </list></summary>
public sealed record ChatMessage(
    string Id, string ThreadId, long Seq, string Kind, string Payload, string? Metadata, DateTimeOffset CreatedAt)
{
    /// <summary>Chat-turn alias for <see cref="Kind"/> (user/assistant/system) — the plain chat shape.</summary>
    public string Role => Kind;

    /// <summary>Chat-turn alias for <see cref="Payload"/> (the message text) — the plain chat shape.</summary>
    public string Content => Payload;
}

/// <summary>Threads + a typed event stream per thread. Deleting a thread cascades to its events.
/// The plain role/text chat shape is the default kind of event; richer typed events (an agent transcript,
/// a tool-loop run) persist through the same surface. An app can also supply its OWN impl over its existing
/// tables (P3) instead of Lyntai's.</summary>
public interface IConversationStore
{
    /// <summary>Create a thread. <paramref name="metadata"/> is optional opaque thread-level JSON state.</summary>
    Task<ChatThread> CreateThreadAsync(string id, string? title = null, string? metadata = null, CancellationToken ct = default);

    Task<ChatThread?> GetThreadAsync(string id, CancellationToken ct = default);

    /// <summary>Threads newest first (created_at DESC, id DESC), at most <paramref name="limit"/> of them,
    /// starting strictly AFTER <paramref name="after"/> when one is given — keyset paging: pass the last thread
    /// of the previous page, or null to start at the newest. A non-positive <paramref name="limit"/> returns
    /// none, on every backend.
    /// <para>The (created_at, id) cursor is stable across same-timestamp ties — the cursor and the ordering
    /// share one collation, so a walk never skips or repeats a thread. That collation is byte-ordinal on
    /// SQLite and in process and the database's on Postgres, so which of two same-timestamp threads comes
    /// first may differ between backends. An in-process store gets the rule from
    /// <see cref="ChatThreads.Page"/>.</para></summary>
    Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, ChatThread? after = null, CancellationToken ct = default);

    /// <summary>Total number of threads in the store — for stats and backlog counts.</summary>
    Task<int> CountThreadsAsync(CancellationToken ct = default);

    /// <summary>Replace a thread's opaque metadata (thread-level state that changes over the run —
    /// e.g. phase/plan/commit projections). No-op if the thread doesn't exist.</summary>
    Task SetThreadMetadataAsync(string id, string? metadata, CancellationToken ct = default);

    /// <summary>Append an event to a thread. <paramref name="kind"/> is the event type (a role for a plain
    /// chat turn); <paramref name="payload"/> is the body (text, or JSON for a richer event);
    /// <paramref name="metadata"/> is optional per-event JSON. The store assigns a GUID <c>Id</c> and the
    /// next per-thread <c>Seq</c>. Appending to a thread that does not exist THROWS on every backend and
    /// stores nothing.</summary>
    Task<ChatMessage> AppendMessageAsync(string threadId, string kind, string payload, string? metadata = null, CancellationToken ct = default);

    /// <summary>Events of a thread in append (sequence) order.</summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default);

    Task DeleteThreadAsync(string id, CancellationToken ct = default);
}

/// <summary><see cref="IConversationStore.ListThreadsAsync"/>'s ordering and cursor rule, for a store that
/// holds its threads in process — the one spelling the shipped in-process stores use, so a BYO one pages the
/// same way.</summary>
public static class ChatThreads
{
    /// <summary>One page: newest first (created_at DESC, then id DESC byte-ordinally), strictly after
    /// <paramref name="after"/> when given, at most <paramref name="limit"/> — none for a non-positive one.</summary>
    /// <param name="threads">Every thread, in any order.</param>
    /// <param name="limit">The most to return.</param>
    /// <param name="after">The last thread of the previous page, or null for the first.</param>
    public static IReadOnlyList<ChatThread> Page(IEnumerable<ChatThread> threads, int limit, ChatThread? after)
    {
        ArgumentNullException.ThrowIfNull(threads);
        if (limit <= 0) return [];
        var q = threads.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id, StringComparer.Ordinal)
            .AsEnumerable();
        if (after is not null)
            q = q.Where(t => t.CreatedAt < after.CreatedAt
                || (t.CreatedAt == after.CreatedAt && string.CompareOrdinal(t.Id, after.Id) < 0));
        return [.. q.Take(limit)];
    }
}

/// <summary>Contributes app-specific ADDITIONAL INFO to a conversation WITHOUT owning the store — Lyntai
/// manages the LLM storage; the app attaches its own info. Register with <c>AddConversationEnricher</c>
/// (a DI collection — add a class + one registration, never a fork); the store invokes every registered
/// enricher AFTER the core row is persisted, so the passed record carries its store-assigned Id/Seq.
/// Persist your info in your OWN store (keyed by the record's Id / thread id). An enricher that throws
/// surfaces to the caller — the core row is already stored — so catch inside for best-effort. Implement
/// only the hook(s) you need (both default to no-op).</summary>
public interface IConversationEnricher
{
    /// <summary>Invoked after a thread is created (with its persisted <see cref="ChatThread"/>).</summary>
    Task OnThreadCreatedAsync(ChatThread thread, CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Invoked after a message is appended (with its persisted <see cref="ChatMessage"/> —
    /// store-assigned Id + per-thread Seq).</summary>
    Task OnMessageAppendedAsync(ChatMessage message, CancellationToken ct = default) => Task.CompletedTask;
}
