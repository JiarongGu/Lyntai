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
/// One retrieval channel — a PRODUCER of candidates, not a policy that varies a per-entry rule, so it is
/// the same category as <see cref="IMemoryGraphStore"/> rather than one of the memory policy domains
/// (<c>docs/DECISIONS.md</c> D47).
///
/// <para><b>PLURAL (D48): implementations COEXIST.</b> Lexical reads text, semantic reads a vector space,
/// subject reads handles — different aspects, all true at once. Register as many as apply; the engine reads
/// the whole collection.</para>
///
/// <para><b>THE RETURN CONTRACT, and it is the whole point of this seam: the list is in THIS source's own
/// best-first order, and position IS the rank. No score crosses this boundary.</b> Two sources cannot share
/// a scale if neither reports one — which is what stops a lexical rank position and a semantic cosine being
/// compared as though they meant the same thing.</para>
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

    /// <summary>Candidates in this source's own best-first order.</summary>
    /// <param name="request">What is being asked for.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Best first; empty when this source has nothing to contribute.</returns>
    Task<IReadOnlyList<GraphNode>> SeedAsync(MemorySeedRequest request, CancellationToken ct);
}
