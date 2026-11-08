using System.Runtime.ExceptionServices;

namespace Lyntai.Storage;

/// <summary>Decorates an <see cref="IConversationStore"/> so registered <see cref="IConversationEnricher"/>s
/// are invoked after a thread is created and after a message is appended — the "add your additional info"
/// seam that composes over ANY backend store (SQLite / Postgres / InMemory / a BYO impl) without replacing
/// it. Every enricher runs even when an earlier one throws; the fault (an <see cref="AggregateException"/>
/// when several threw) is then rethrown, the row already stored. Auto-wired by <c>AddLyntai</c> only when
/// at least one enricher is registered (otherwise the plain backend store resolves unwrapped).</summary>
public sealed class EnrichingConversationStore(IConversationStore inner, IEnumerable<IConversationEnricher> enrichers)
    : IConversationStore
{
    private readonly IReadOnlyList<IConversationEnricher> _enrichers = [.. enrichers];

    public async Task<ChatThread> CreateThreadAsync(string id, string? title = null, string? metadata = null, CancellationToken ct = default)
    {
        var thread = await inner.CreateThreadAsync(id, title, metadata, ct).ConfigureAwait(false);
        await RunAsync(e => e.OnThreadCreatedAsync(thread, ct), ct).ConfigureAwait(false);
        return thread;
    }

    public async Task<ChatMessage> AppendMessageAsync(string threadId, string kind, string payload, string? metadata = null, CancellationToken ct = default)
    {
        var message = await inner.AppendMessageAsync(threadId, kind, payload, metadata, ct).ConfigureAwait(false);
        await RunAsync(e => e.OnMessageAppendedAsync(message, ct), ct).ConfigureAwait(false);
        return message;
    }

    // The row is stored before any enricher runs, so one fault must not skip the rest: a caller's retry
    // would append a duplicate. The CALLER's cancel is not a fault and ends the pass at once.
    private async Task RunAsync(Func<IConversationEnricher, Task> hook, CancellationToken ct)
    {
        List<Exception>? faults = null;
        foreach (var e in _enrichers)
        {
            try { await hook(e).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) { (faults ??= []).Add(ex); }
        }
        if (faults is null) return;
        if (faults.Count == 1) ExceptionDispatchInfo.Capture(faults[0]).Throw();
        throw new AggregateException(faults);
    }

    public Task<ChatThread?> GetThreadAsync(string id, CancellationToken ct = default) => inner.GetThreadAsync(id, ct);

    public Task<IReadOnlyList<ChatThread>> ListThreadsAsync(int limit = 100, ChatThread? after = null, CancellationToken ct = default) => inner.ListThreadsAsync(limit, after, ct);

    public Task<int> CountThreadsAsync(CancellationToken ct = default) => inner.CountThreadsAsync(ct);

    public Task SetThreadMetadataAsync(string id, string? metadata, CancellationToken ct = default) => inner.SetThreadMetadataAsync(id, metadata, ct);

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(string threadId, CancellationToken ct = default) => inner.GetMessagesAsync(threadId, ct);

    public Task DeleteThreadAsync(string id, CancellationToken ct = default) => inner.DeleteThreadAsync(id, ct);
}
