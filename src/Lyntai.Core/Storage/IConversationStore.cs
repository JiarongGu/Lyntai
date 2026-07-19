namespace Lyntai.Storage;

/// <summary>A conversation thread. (Named ChatThread — plain "Thread" collides with System.Threading.)
/// <paramref name="Metadata"/> is optional opaque thread-level state (a small JSON blob the app owns —
/// e.g. a phase/plan projection) so an app needn't bolt on a bespoke per-thread column.</summary>
public sealed record ChatThread(string Id, string? Title, DateTimeOffset CreatedAt, string? Metadata = null);

/// <summary>One event in a thread's stream. A conversation is, in general, a typed multi-kind event stream
/// (text / tool-call / tool-result / usage / thinking / phase / error), not only role/text chat turns —
/// so <paramref name="Kind"/> is a free-form event type (chat uses a role: user/assistant/system/tool) and
/// <paramref name="Payload"/> is the event body (chat uses the message text; richer events carry JSON).
/// <paramref name="Id"/> is the store-assigned sequence.</summary>
public sealed record ChatMessage(long Id, string ThreadId, string Kind, string Payload, DateTimeOffset CreatedAt)
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

    Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, CancellationToken ct = default);

    /// <summary>Replace a thread's opaque metadata (thread-level state that changes over the run —
    /// e.g. phase/plan/commit projections). No-op if the thread doesn't exist.</summary>
    Task SetThreadMetadataAsync(string id, string? metadata, CancellationToken ct = default);

    /// <summary>Append an event to a thread. <paramref name="kind"/> is the event type (a role for a plain
    /// chat turn); <paramref name="payload"/> is the body (text, or JSON for a richer event).</summary>
    Task<ChatMessage> AppendMessageAsync(string threadId, string kind, string payload, CancellationToken ct = default);

    /// <summary>Events of a thread in append (sequence) order.</summary>
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default);

    Task DeleteThreadAsync(string id, CancellationToken ct = default);
}
