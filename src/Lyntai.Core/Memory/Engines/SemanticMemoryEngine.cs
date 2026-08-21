using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Engines;

/// <summary>Adapts meaning-based <see cref="ISemanticMemory"/> to <see cref="IMemoryEngine"/>. Associative
/// only, so an authoritative write is REFUSED rather than downgraded.
/// <para>A recall with no query to embed yields nothing rather than throwing — the same fail-open posture
/// the rest of this seam takes. A recall with no SCOPE searches every scope of the task, which is what a
/// consumer treating scope as an optional filter means by it; that needs an
/// <see cref="IListableVectorStore"/> underneath and yields nothing without one.</para></summary>
/// <param name="name">This engine's name, hierarchical when it is a member of a composite.</param>
/// <param name="semantic">The semantic memory to draw on.</param>
/// <param name="defaultK">How many hits to ask for when the query carries no limit.</param>
/// <param name="logger">Optional; recall failures are logged rather than thrown.</param>
public sealed class SemanticMemoryEngine(
    string name,
    ISemanticMemory semantic,
    int defaultK = 10,
    ILogger<SemanticMemoryEngine>? logger = null) : IMemoryEngine, IForgettableMemory
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
        // BEFORE the early return below, or a cancelled token would be swallowed by an empty query
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(query.Query)) return MemoryRecall.Empty;

        try
        {
            var hits = await semantic
                .RecallAsync(query.TaskKey, query.Scope, query.Query, query.Limit ?? defaultK, ct: ct)
                .ConfigureAwait(false);
            if (hits.Count == 0) return MemoryRecall.Empty;

            var items = new List<MemoryItem>(hits.Count);
            foreach (var hit in hits)
            {
                // the hit's own scope on a cross-scope recall, the caller's otherwise — the id must be the
                // one RememberAsync minted for this (task, scope, content), or the ref addresses nothing
                var scope = hit.Scope ?? query.Scope ?? string.Empty;
                items.Add(new MemoryItem(
                    new MemoryRef(Name, MemoryContentId.For(query.TaskKey, scope, hit.Content)),
                    hit.Content, hit.Content, MemoryGrade.Associative,
                    Math.Clamp(hit.Score, 0, 1), 1, 0));
            }

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

    /// <inheritdoc />
    /// <remarks><b>A null scope THROWS, and that is the contract's own instruction rather than a shortcut.</b>
    /// <see cref="ISemanticMemory.ForgetAsync"/> requires a scope — a vector store is addressed by
    /// (taskKey, scope) and cannot enumerate the scopes a task has used — so "forget every scope" is
    /// inexpressible here. The alternatives are both worse: forgetting nothing would report success while the
    /// embeddings remain, and forgetting some arbitrary scope would delete the wrong data. A consent
    /// withdrawal that silently does less than it says is the failure this surface exists to prevent, so it
    /// fails LOUDLY and names the fix.
    /// <para>This engine deliberately does NOT implement <see cref="IPrunableMemory"/> — see the class
    /// remarks. Before 3.0 split the two capabilities it could not have made that distinction.</para></remarks>
    public Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        if (scope is null)
            throw new NotSupportedException(
                $"Memory engine '{Name}' is backed by a vector store addressed by (task, scope) and cannot " +
                "forget every scope of a task at once — it has no way to enumerate which scopes exist. Call " +
                "ForgetAsync once per scope you know of. Nothing was removed, deliberately: reporting " +
                "success here would leave the embeddings in place.");

        return semantic.ForgetAsync(taskKey, scope, ct);
    }
}
