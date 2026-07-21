namespace Lyntai.Storage;

/// <summary>Decorates an <see cref="IConversationStore"/> so registered <see cref="IConversationEnricher"/>s
/// are invoked after each write — the "add your additional info" seam that composes over ANY backend store
/// (SQLite / Postgres / InMemory / a BYO impl) without replacing it. Auto-wired by <c>AddLyntai</c> only when
/// at least one enricher is registered (otherwise the plain backend store resolves unwrapped).</summary>
public sealed class EnrichingConversationStore(IConversationStore inner, IEnumerable<IConversationEnricher> enrichers)
    : IConversationStore
{
    private readonly IReadOnlyList<IConversationEnricher> _enrichers = [.. enrichers];

    public async Task<ChatThread> CreateThreadAsync(string id, string? title = null, string? metadata = null, CancellationToken ct = default)
    {
        var thread = await inner.CreateThreadAsync(id, title, metadata, ct).ConfigureAwait(false);
        foreach (var e in _enrichers) await e.OnThreadCreatedAsync(thread, ct).ConfigureAwait(false);
        return thread;
    }

    public async Task<ChatMessage> AppendMessageAsync(string threadId, string kind, string payload, string? metadata = null, CancellationToken ct = default)
    {
        var message = await inner.AppendMessageAsync(threadId, kind, payload, metadata, ct).ConfigureAwait(false);
        foreach (var e in _enrichers) await e.OnMessageAppendedAsync(message, ct).ConfigureAwait(false);
        return message;
    }

    public Task<ChatThread?> GetThreadAsync(string id, CancellationToken ct = default) => inner.GetThreadAsync(id, ct);

    public Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, CancellationToken ct = default) => inner.ListThreadsAsync(limit, ct);

    public Task<int> CountThreadsAsync(CancellationToken ct = default) => inner.CountThreadsAsync(ct);

    public Task<IReadOnlyList<ChatThread>> ListThreadsPageAsync(int limit, ChatThread? after = null, CancellationToken ct = default) => inner.ListThreadsPageAsync(limit, after, ct);

    public Task SetThreadMetadataAsync(string id, string? metadata, CancellationToken ct = default) => inner.SetThreadMetadataAsync(id, metadata, ct);

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default) => inner.GetMessagesAsync(threadId, ct);

    public Task DeleteThreadAsync(string id, CancellationToken ct = default) => inner.DeleteThreadAsync(id, ct);
}
