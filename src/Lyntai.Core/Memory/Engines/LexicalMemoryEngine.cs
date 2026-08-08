using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>Adapts the keyword <see cref="IMemoryStore"/> to <see cref="IMemoryEngine"/>. Associative only:
/// the store has no grade column, so an authoritative write is REFUSED rather than downgraded.
/// <para>Entries are addressed by content hash, because the store's write returns no identifier and its own
/// notion of identity is exact content (re-remembering the same fact refreshes it rather than
/// duplicating).</para></summary>
/// <param name="name">This engine's name, hierarchical when it is a member of a composite.</param>
/// <param name="store">The keyword store to draw on.</param>
/// <param name="logger">Optional; recall failures are logged rather than thrown.</param>
public sealed class LexicalMemoryEngine(
    string name,
    IMemoryStore store,
    ILogger<LexicalMemoryEngine>? logger = null) : IMemoryEngine
{
    private readonly ILogger _logger = logger ?? NullLogger<LexicalMemoryEngine>.Instance;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public MemoryGrades Supported => MemoryGrades.Associative;

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (write.Grade == MemoryGrade.Authoritative)
            throw new NotSupportedException(
                $"Memory engine '{Name}' stores associative material only and cannot hold an authoritative " +
                "write. Route it to an engine whose Supported includes Authoritative, or add one to the " +
                "composite.");

        await store.RememberAsync(write.TaskKey, write.Scope, write.Content, ct: ct).ConfigureAwait(false);
        return new MemoryRef(Name, MemoryContentId.For(write.TaskKey, write.Scope, write.Content));
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        try
        {
            var entries = await store
                .RecallAsync(query.TaskKey, query.Scope, query.Query, query.Limit, ct)
                .ConfigureAwait(false);
            if (entries.Count == 0) return MemoryRecall.Empty;

            var items = new List<MemoryItem>(entries.Count);
            foreach (var entry in entries)
                items.Add(new MemoryItem(
                    new MemoryRef(Name, MemoryContentId.For(entry.TaskKey, entry.Scope, entry.Content)),
                    entry.Content, entry.Content, MemoryGrade.Associative, 1, 1, 0));

            return new MemoryRecall(items, MemorySources.Lexical);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            // recall is contractually fail-open — a broken custom store must not sink the caller's prompt
            _logger.LogWarning(ex, "lexical recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }
    }
}
