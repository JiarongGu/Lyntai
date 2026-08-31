namespace Lyntai.Memory.Seeding;

/// <summary>What one seed source is asked for. The STORE rides here rather than being injected because it
/// is per-engine (a constructor parameter of <c>GraphMemoryEngine</c>) while a registered source is a
/// singleton — a DI-resolved source cannot otherwise know which store it is reading.</summary>
/// <param name="Engine">The calling engine's name.</param>
/// <param name="Store">The graph store this engine reads.</param>
/// <param name="Query">The recall being served.</param>
/// <param name="Limit">How many candidates this source may return at most.</param>
public readonly record struct MemorySeedRequest(
    string Engine, IMemoryGraphStore Store, MemoryQuery Query, int Limit);

/// <summary>
/// One retrieval channel — a PRODUCER of candidates, not a policy varying a per-entry rule, so it is the
/// same category as <see cref="IMemoryGraphStore"/> rather than a memory policy domain (D47). <b>PLURAL
/// (D48): implementations COEXIST</b> — lexical, semantic and subject read different aspects, all true at
/// once. Register as many as apply; the engine reads the whole collection.
///
/// <para><b>THE RETURN CONTRACT: a source is ranked by its OWN <see cref="GraphNode.Relevance"/> gradient,
/// never by list POSITION.</b> Higher is better, and the engine competition-ranks the matched nodes by it —
/// equal values share a rank, the next distinct value skips the tied group's width.
/// <list type="bullet">
/// <item>A node this source did not match reports <see cref="GraphNode.Matched"/> <c>false</c> and is left
/// unranked, whatever its position.</item>
/// <item><b>Matched nodes ALL on one value means "I have no ordering", not "everything is maximally
/// relevant"</b> — the source contributes no relevance evidence. Return a flat value when that is the truth
/// about your channel; do not fabricate a gradient to avoid it.</item>
/// <item>A single matched node is that source's top hit and takes rank 1.</item>
/// </list>
/// The value is WITHIN-SOURCE and no score crosses this boundary: two sources' values are never compared,
/// which stops a lexical ramp and a semantic cosine being read as one scale.</para>
///
/// <para><b>Best-effort.</b> A source that cannot answer returns empty; it must not throw for a transient
/// fault, because the engine gathers sources in sequence and a throw would discard the candidates the
/// others already produced. Cancellation is the exception and is always propagated.</para>
/// </summary>
public interface IMemorySeedSource
{
    /// <summary>This channel's stable name, used as the key in
    /// <see cref="MemorySeedRanks"/>. Ordinal comparison, so it is case-sensitive.</summary>
    string Name { get; }

    /// <summary>This channel's candidates, each carrying its own <see cref="GraphNode.Relevance"/> — see the
    /// type's remarks for what the engine does with that value and what a flat one means.</summary>
    /// <param name="request">What is being asked for.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Empty when this source has nothing to contribute. List ORDER is not read; the relevance
    /// gradient is.</returns>
    Task<IReadOnlyList<GraphNode>> SeedAsync(MemorySeedRequest request, CancellationToken ct);
}
