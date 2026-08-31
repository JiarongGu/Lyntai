using System.Globalization;
using Lyntai.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Seeding;

/// <summary>The vector channel: embeds the query and searches an <see cref="IVectorStore"/> for its nearest
/// entries, contributing each as a candidate in COSINE order. Registered nowhere by default, unlike
/// <see cref="LexicalSeedSource"/> — an embedder is wired for reasons of its own, so seeding recall from it
/// is an opt-in (<c>AddMemorySemanticSeeds</c>).
///
/// <para><b>Best-effort, per the seam's own contract</b> (<see cref="IMemorySeedSource"/>): a failing embedder
/// or vector store logs a warning and returns empty rather than throwing, so this channel's outage degrades
/// QUALITY and never CORRECTNESS. <see cref="OperationCanceledException"/> is the one exception never
/// swallowed — that is the caller leaving, not an enrichment fault.</para>
///
/// <para><b>A null <see cref="MemoryQuery.Scope"/> spans every collection the vector store holds under the
/// task</b>, agreeing with what an unscoped LEXICAL seed already means: a write always names a scope, so the
/// single literal collection a null scope would otherwise address can never exist. Spanning needs an
/// <see cref="IListableVectorStore"/>; a store without it contributes nothing on the unscoped path, exactly
/// as a scoped query on the same store still would.</para>
///
/// <para><b>Own best-first order, imposed rather than borrowed.</b> <see cref="IVectorStore.SearchAsync"/>
/// leaves ties between equal scores UNSPECIFIED, so this source re-orders every match itself — score
/// descending, then id ordinally — before handing any of it back as rank.</para></summary>
public sealed class SemanticSeedSource(
    IEmbedder embedder,
    IVectorStore vectors,
    SemanticSeedOptions? options = null,
    ILogger<SemanticSeedSource>? logger = null) : IMemorySeedSource
{
    private readonly SemanticSeedOptions _options = options ?? new SemanticSeedOptions();
    private readonly ILogger _logger = logger ?? NullLogger<SemanticSeedSource>.Instance;

    /// <inheritdoc />
    public string Name => "semantic";

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> SeedAsync(MemorySeedRequest request, CancellationToken ct)
    {
        // Bounded by BOTH this source's own SemanticSeedOptions.K and the caller's MemorySeedRequest.Limit —
        // the seam's own contract says a source may return at most Limit, and this source additionally caps
        // its own fan-out so a recall's candidate budget and this channel's search width can be tuned apart.
        var k = Math.Min(_options.K, request.Limit);
        if (k <= 0 || string.IsNullOrWhiteSpace(request.Query.Query)) return [];

        IReadOnlyList<VectorMatch> near;
        try
        {
            var vector = await embedder.EmbedAsync(request.Query.Query, ct).ConfigureAwait(false);
            near = request.Query.Scope is null
                ? await AcrossScopesAsync(vector, request.Engine, request.Query.TaskKey, k, ct)
                    .ConfigureAwait(false)
                : await vectors.SearchAsync(
                    Collection(request.Engine, request.Query.TaskKey, request.Query.Scope), vector, k, ct)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "semantic seeding failed for {Engine}; returning no semantic candidates",
                request.Engine);
            return [];
        }

        var ordered = near
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Take(k);

        var results = new List<GraphNode>(k);
        var seen = new HashSet<long>();
        foreach (var match in ordered)
        {
            if (!long.TryParse(match.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            if (!seen.Add(id)) continue;
            var node = await request.Store.GetAsync(request.Engine, id, ct).ConfigureAwait(false);
            if (node is not null) results.Add(node);
        }
        return results;
    }

    /// <summary>Search every collection the vector store holds under <paramref name="taskKey"/> and merge —
    /// one search per collection, each already bounded by <paramref name="k"/>. Returns the raw merge
    /// unordered; <see cref="SeedAsync"/> applies the final score/id order and the overall
    /// <paramref name="k"/> cap once, after either branch produces its matches.</summary>
    private async Task<IReadOnlyList<VectorMatch>> AcrossScopesAsync(float[] vector, string engine,
        string taskKey, int k, CancellationToken ct)
    {
        if (vectors is not IListableVectorStore listable) return [];

        var prefix = $"{engine}|{taskKey}|";
        var merged = new List<VectorMatch>();
        foreach (var collection in await listable.ListCollectionsAsync(prefix, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            merged.AddRange(await vectors.SearchAsync(collection, vector, k, ct).ConfigureAwait(false));
        }
        return merged;
    }

    /// <summary>The same <c>{engine}|{taskKey}|{scope}</c> key <c>GraphMemoryEngine</c> writes vectors under,
    /// so a collection this source searches is exactly one an enrichment write already populated.</summary>
    private static string Collection(string engine, string taskKey, string scope) => $"{engine}|{taskKey}|{scope}";
}
