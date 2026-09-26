using Lyntai.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory;

/// <summary>Default <see cref="ISemanticMemory"/>: embeds content/queries with the registered
/// backend declaring <see cref="ProviderKinds.Vector"/> and stores/searches vectors through an
/// <see cref="IVectorStore"/> (one collection per task+scope). Embedding is optional at construction so the service always resolves,
/// but a call throws a clear error if none was registered.</summary>
public sealed class SemanticMemory(
    IEnumerable<IModelProvider>? providers, IVectorStore vectors,
    ILogger<SemanticMemory>? logger = null,
    IProviderRouterFactory? routing = null) : ISemanticMemory
{
    private readonly ILogger _logger = logger ?? NullLogger<SemanticMemory>.Instance;

    private int _warnedUnlistable;

    // The full provider list, guarded. Routing filters it again and would throw on its own, so this exists
    // only to put "semantic memory" in front of that message — a consumer who wired this deliberately is
    // owed the reason it cannot run, not just the generic one. Named for what it returns, not for what it
    // checks: these are the registered backends, not a pre-filtered set of vector backends.
    private IEnumerable<IModelProvider> ProvidersOrThrow => EmbeddingRouting.CanEmbed(providers)
        ? providers!
        : throw new InvalidOperationException($"Semantic memory needs to embed. {EmbeddingRouting.NothingEmbeds}");

    public async Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default)
    {
        // NOT fail-open (unlike RecallAsync): a write that faults SURFACES rather than silently losing the
        // fact — see the ISemanticMemory.RememberAsync contract. After an embedding-MODEL swap the stored
        // vectors keep their old dimension, and every shipped store scores a mismatched row 0 and ranks it last,
        // so REINDEX (ForgetAsync + re-Remember).
        if (string.IsNullOrWhiteSpace(content)) return;
        var vector = await EmbeddingRouting.EmbedOneAsync(
            ProvidersOrThrow, content, EmbeddingRole.Document, _logger, routing,
            ProviderConsumers.Memory, ct).ConfigureAwait(false);
        await vectors.UpsertAsync(Collection(taskKey, scope), IdFor(content), vector, content, ct).ConfigureAwait(false);
        _logger.LogDebug("semantic memory: remembered {Chars} chars in {Task}/{Scope}", content.Length, taskKey, scope);
    }

    public async Task<IReadOnlyList<SemanticHit>> RecallAsync(string taskKey, string? scope, string query,
        int k = 5, double minScore = 0, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query) || k <= 0) return [];
        try
        {
            var qv = await EmbeddingRouting.EmbedOneAsync(
                ProvidersOrThrow, query, EmbeddingRole.Query, _logger, routing,
                ProviderConsumers.Memory, ct).ConfigureAwait(false);
            if (scope is null) return await AcrossScopesAsync(taskKey, qv, k, minScore, ct).ConfigureAwait(false);
            var matches = await vectors.SearchAsync(Collection(taskKey, scope), qv, k, ct).ConfigureAwait(false);
            return [.. matches.Where(m => m.Score >= minScore).Select(m => new SemanticHit(m.Payload, m.Score))];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // fail-open (like lexical recall): a BYO vector backend may throw on a dimension-mismatched row after
            // an embedding-model swap — degrade to no hits, don't take down the caller. REINDEX (Forget +
            // re-Remember, or drop the collection) after changing the model.
            _logger.LogWarning(ex, "semantic recall failed for {Task}/{Scope} — returning empty (reindex after a model change)", taskKey, scope);
            return [];
        }
    }

    /// <summary>Search every collection under the task and merge — one search per scope, each already bounded
    /// by <paramref name="k"/>, so the global top-k is a subset of the per-scope top-k's and nothing is
    /// missed by merging afterwards.</summary>
    /// <remarks>Ties break by scope then content, ordinally. Without that, two equally-scoring entries in
    /// different scopes swap places between runs and, at the k boundary, one silently drops out — the defect
    /// <c>VectorStoreContract.Equal_scores_are_ordered_by_id</c> pins one layer down, reintroduced here by
    /// the merge.</remarks>
    private async Task<IReadOnlyList<SemanticHit>> AcrossScopesAsync(string taskKey, float[] qv, int k,
        double minScore, CancellationToken ct)
    {
        if (vectors is not IListableVectorStore listable)
        {
            // once per store: it is a wiring fact, not a per-call event, and a scope-less recall is on the
            // hot path of every prompt an application composes
            if (Interlocked.Exchange(ref _warnedUnlistable, 1) == 0)
                _logger.LogWarning(
                    "semantic memory: a recall with no scope searches every scope of the task, which needs " +
                    "IListableVectorStore; {Store} cannot enumerate its collections, so nothing was searched",
                    vectors.GetType().Name);
            return [];
        }

        var prefix = MemoryVectorCollection.SemanticPrefixFor(taskKey);
        var collections = await listable.ListCollectionsAsync(prefix, ct).ConfigureAwait(false);

        var merged = new List<SemanticHit>();
        foreach (var collection in collections)
        {
            // a graph engine named like this task shares the prefix; its collections are another task's
            if (MemoryVectorCollection.SemanticScopeOf(collection, prefix) is not { } scope) continue;
            ct.ThrowIfCancellationRequested();
            var matches = await vectors.SearchAsync(collection, qv, k, ct).ConfigureAwait(false);
            merged.AddRange(matches
                .Where(m => m.Score >= minScore)
                .Select(m => new SemanticHit(m.Payload, m.Score) { Scope = scope }));
        }

        return [.. merged
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Scope, StringComparer.Ordinal)
            .ThenBy(h => h.Content, StringComparer.Ordinal)
            .Take(k)];
    }

    public Task ForgetAsync(string taskKey, string scope, CancellationToken ct = default) =>
        vectors.RemoveCollectionAsync(Collection(taskKey, scope), ct);

    private static string Collection(string taskKey, string scope) => MemoryVectorCollection.ForSemantic(taskKey, scope);

    // the content key is the vector id, so re-remembering identical content overwrites (dedup)
    private static string IdFor(string content) => MemoryContentKey.Of(content);
}
