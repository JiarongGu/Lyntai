namespace Lyntai.Memory.Seeding;

/// <summary>The store's own text read — the channel every graph engine has, registered unconditionally by
/// <c>AddMemoryEngine</c> because <see cref="IMemoryGraphStore.SeedAsync"/> was unconditional before this
/// seam existed.
/// <para><b>Returns the store's rows untouched</b>, <see cref="GraphNode.Relevance"/> included — so this
/// channel is ranked by whatever gradient the backend computed. SQLite and Postgres normalize a real one;
/// <b>the in-process store reports a flat <c>1</c> for every match and is therefore treated as UNORDERED</b>,
/// contributing no relevance evidence, which is the honest reading of a store whose own contract says it has
/// no rank order — <b>except at exactly one match</b>, where there is no tie to read and the single eligible
/// node takes rank 1 by the same rule every source follows at that cardinality
/// (<see cref="IMemorySeedSource"/>'s own remarks). The engine, not this source, applies that rule and the
/// <see cref="GraphNode.Matched"/>-<c>false</c> one — see <c>GraphMemoryEngine.SourceRanks</c>.</para></summary>
public sealed class LexicalSeedSource : IMemorySeedSource
{
    /// <inheritdoc />
    public string Name => "lexical";

    /// <inheritdoc />
    public Task<IReadOnlyList<GraphNode>> SeedAsync(MemorySeedRequest request, CancellationToken ct) =>
        request.Store.SeedAsync(request.Engine, request.Query.TaskKey, request.Query.Scope,
            request.Query.Query, request.Limit, ct);
}
