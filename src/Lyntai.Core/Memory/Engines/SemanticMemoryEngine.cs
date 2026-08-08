using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>Adapts meaning-based <see cref="ISemanticMemory"/> to <see cref="IMemoryEngine"/>. Associative
/// only, so an authoritative write is REFUSED rather than downgraded.
/// <para><see cref="ISemanticMemory"/> needs a concrete scope (a vector collection is per task+scope) and a
/// non-empty query to embed, so a recall missing either yields nothing rather than throwing — the same
/// fail-open posture the rest of this seam takes.</para></summary>
/// <param name="name">This engine's name, hierarchical when it is a member of a composite.</param>
/// <param name="semantic">The semantic memory to draw on.</param>
/// <param name="defaultK">How many hits to ask for when the query carries no limit.</param>
/// <param name="logger">Optional; recall failures are logged rather than thrown.</param>
public sealed class SemanticMemoryEngine(
    string name,
    ISemanticMemory semantic,
    int defaultK = 10,
    ILogger<SemanticMemoryEngine>? logger = null) : IMemoryEngine
{
    private readonly ILogger _logger = logger ?? NullLogger<SemanticMemoryEngine>.Instance;

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

        await semantic.RememberAsync(write.TaskKey, write.Scope, write.Content, ct).ConfigureAwait(false);
        return new MemoryRef(Name, MemoryContentId.For(write.TaskKey, write.Scope, write.Content));
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        // BEFORE the early returns below, or a cancelled token would be swallowed by a missing scope
        ct.ThrowIfCancellationRequested();
        if (query.Scope is null || string.IsNullOrWhiteSpace(query.Query)) return MemoryRecall.Empty;

        try
        {
            var hits = await semantic
                .RecallAsync(query.TaskKey, query.Scope, query.Query, query.Limit ?? defaultK, ct: ct)
                .ConfigureAwait(false);
            if (hits.Count == 0) return MemoryRecall.Empty;

            var items = new List<MemoryItem>(hits.Count);
            foreach (var hit in hits)
                items.Add(new MemoryItem(
                    new MemoryRef(Name, MemoryContentId.For(query.TaskKey, query.Scope, hit.Content)),
                    hit.Content, hit.Content, MemoryGrade.Associative,
                    Math.Clamp(hit.Score, 0, 1), 1, 0));

            return new MemoryRecall(items, MemorySources.Semantic);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "semantic recall failed for {Engine}/{Task}; returning nothing",
                Name, query.TaskKey);
            return MemoryRecall.Empty;
        }
    }
}
