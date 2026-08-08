using System.Globalization;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>Adapts the operator-curated <see cref="ICuratedMemoryStore"/> to <see cref="IMemoryEngine"/>.
/// AUTHORITATIVE by construction — the catalog is deliberately managed, has no cap and no TTL, so its
/// entries are exact facts. It therefore REFUSES an associative write: accepting one would let decaying
/// material into the section composition renders as authoritative, which is the precise confusion the
/// grade split exists to prevent.
/// <para>Writes go in under <paramref name="kind"/> with <c>dedup: true</c>, so remembering the same fact
/// twice is idempotent rather than minting a duplicate catalog row.</para></summary>
/// <param name="name">This engine's name, hierarchical when it is a member of a composite.</param>
/// <param name="store">The catalog to draw on.</param>
/// <param name="kind">The catalog section this engine reads and writes.</param>
/// <param name="logger">Optional; recall failures are logged rather than thrown.</param>
public sealed class CuratedMemoryEngine(
    string name,
    ICuratedMemoryStore store,
    string kind = "memory",
    ILogger<CuratedMemoryEngine>? logger = null) : IMemoryEngine
{
    private readonly ILogger _logger = logger ?? NullLogger<CuratedMemoryEngine>.Instance;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <inheritdoc />
    public MemoryGrades Supported => MemoryGrades.Authoritative;

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (write.Grade == MemoryGrade.Associative)
            throw new NotSupportedException(
                $"Memory engine '{Name}' is a curated catalog and holds authoritative material only. Route " +
                "associative material to an engine whose Supported includes Associative.");

        var id = await store.AddAsync(kind, write.Content, enabled: true, taskKey: write.TaskKey,
            scope: write.Scope, dedup: true, metadata: write.Metadata, ct: ct).ConfigureAwait(false);
        return new MemoryRef(Name, id.ToString(CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();
        try
        {
            // no query = "everything that applies to this task", which is the catalog's composition read
            var entries = string.IsNullOrWhiteSpace(query.Query)
                ? await store.ForCompositionAsync(query.TaskKey,
                        query.Scope is null ? [] : [query.Scope], enabledOnly: true, ct)
                    .ConfigureAwait(false)
                : await store.SearchAsync(query.Query, kind, query.TaskKey, query.Scope,
                    enabledOnly: true, query.Limit, ct: ct).ConfigureAwait(false);
            if (entries.Count == 0) return MemoryRecall.Empty;

            var items = new List<MemoryItem>(entries.Count);
            foreach (var entry in entries)
                items.Add(new MemoryItem(
                    new MemoryRef(Name, entry.Id.ToString(CultureInfo.InvariantCulture)),
                    entry.Content, entry.Content, MemoryGrade.Authoritative, 1, 1, 0));

            return new MemoryRecall(items, MemorySources.Curated);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "curated recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }
    }
}
