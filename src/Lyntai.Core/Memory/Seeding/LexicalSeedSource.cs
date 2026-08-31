namespace Lyntai.Memory.Seeding;

/// <summary>The store's own text read — the channel every graph engine has, registered unconditionally by
/// <c>AddMemoryEngine</c> because <see cref="IMemoryGraphStore.SeedAsync"/> was unconditional before this
/// seam existed.
/// <para><b>Returns the store's order untouched</b>, which is grade-first then by the backend's own
/// relevance gradient. The engine, not this source, decides that a
/// <see cref="GraphNode.Matched"/>-<c>false</c> node earns no rank — see
/// <c>GraphMemoryEngine.GatherAsync</c>.</para></summary>
public sealed class LexicalSeedSource : IMemorySeedSource
{
    /// <inheritdoc />
    public string Name => "lexical";

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNode>> SeedAsync(MemorySeedRequest request, CancellationToken ct) =>
        request.Store.SeedAsync(request.Engine, request.Query.TaskKey, request.Query.Scope,
            request.Query.Query, request.Limit, ct);
}
