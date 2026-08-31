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
/// <para><b>A null <see cref="MemoryQuery.Scope"/> spans every collection the store holds under the task</b>,
/// agreeing with what an unscoped LEXICAL seed means: a write always names a scope, so the single literal
/// collection a null scope would address can never exist. Spanning needs an
/// <see cref="IListableVectorStore"/>; a store without it contributes nothing there.</para>
///
/// <para><b>Each returned node carries its clamped COSINE as <see cref="GraphNode.Relevance"/> and
/// <see cref="GraphNode.Matched"/> <c>true</c>.</b> That is this channel's WITHIN-SOURCE gradient, the thing
/// the engine ranks it by (<see cref="IMemorySeedSource"/>), and never a portable score. Without it every
/// node would carry <see cref="IMemoryGraphStore.GetAsync"/>'s <c>Relevance 0</c>, the channel would read as
/// UNORDERED, and it would contribute no relevance evidence at all.</para>
///
/// <para><b>Two bounds on two different things.</b> <see cref="SemanticSeedOptions.K"/> is the SEARCH width;
/// <see cref="MemorySeedRequest.Limit"/> additionally caps what is RETURNED. Coupling them would narrow the
/// search itself whenever a recall's limit is smaller. The cap is deterministic because this source sorts by
/// score then id ordinally first — <see cref="IVectorStore.SearchAsync"/> leaves ties UNSPECIFIED.</para></summary>
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
        if (request.Limit <= 0 || string.IsNullOrWhiteSpace(request.Query.Query)) return [];

        IReadOnlyList<VectorMatch> near;
        try
        {
            var vector = await embedder.EmbedAsync(request.Query.Query, ct).ConfigureAwait(false);
            // SEARCH width is _options.K alone, exactly what today's engine passes to every SearchAsync call
            // — never narrowed by request.Limit, or a small recall limit would silently shrink what this
            // source can ever consider before RETURN even enters the picture.
            near = request.Query.Scope is null
                ? await AcrossScopesAsync(vector, request.Engine, request.Query.TaskKey, ct)
                    .ConfigureAwait(false)
                : await vectors.SearchAsync(
                    Collection(request.Engine, request.Query.TaskKey, request.Query.Scope), vector, _options.K, ct)
                    .ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "semantic seeding failed for {Engine}; returning no semantic candidates",
                request.Engine);
            return [];
        }

        // RETURN is capped by both — MemorySeedRequest.Limit's own contract ("may return at most") applies
        // here regardless of how wide the search was.
        var take = Math.Min(_options.K, request.Limit);
        var ordered = near
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Take(take);

        var results = new List<GraphNode>(take);
        var seen = new HashSet<long>();
        foreach (var match in ordered)
        {
            if (!long.TryParse(match.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
            if (!seen.Add(id)) continue;
            var node = await request.Store.GetAsync(request.Engine, id, ct).ConfigureAwait(false);
            // The cosine rides out as this source's own gradient — see the type's remarks. A fetch by id
            // reports Relevance 0 / Matched null ("nobody asked"), and this read DID ask.
            if (node is not null)
                results.Add(node with { Relevance = Math.Clamp(match.Score, 0, 1), Matched = true });
        }
        return results;
    }

    /// <summary>Search every collection the vector store holds under <paramref name="taskKey"/> and merge —
    /// one search per collection, each already bounded by <see cref="SemanticSeedOptions.K"/>. Returns the
    /// raw merge unordered; <see cref="SeedAsync"/> applies the final score/id order and the
    /// request-and-option cap once, after either branch produces its matches.</summary>
    private async Task<IReadOnlyList<VectorMatch>> AcrossScopesAsync(float[] vector, string engine,
        string taskKey, CancellationToken ct)
    {
        if (vectors is not IListableVectorStore listable) return [];

        var prefix = $"{engine}|{taskKey}|";
        var merged = new List<VectorMatch>();
        foreach (var collection in await listable.ListCollectionsAsync(prefix, ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            merged.AddRange(await vectors.SearchAsync(collection, vector, _options.K, ct).ConfigureAwait(false));
        }
        return merged;
    }

    /// <summary>The same <c>{engine}|{taskKey}|{scope}</c> key <c>GraphMemoryEngine</c> writes vectors under,
    /// so a collection this source searches is exactly one an enrichment write already populated.</summary>
    private static string Collection(string engine, string taskKey, string scope) => $"{engine}|{taskKey}|{scope}";
}
