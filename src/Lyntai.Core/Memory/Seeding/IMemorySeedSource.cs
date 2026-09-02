namespace Lyntai.Memory.Seeding;

/// <summary>What a retrieval channel IS, as opposed to what it is called. The wiring diagnostics ask this
/// rather than matching <see cref="IMemorySeedSource.Name"/>, so a consumer's own channel can be named for
/// their product and still answer "yes, the semantic index is reachable".
/// <para><see cref="Custom"/> is the default and means "not stated". It is not a fourth role — it is the
/// absence of one, and it makes a diagnostic abstain rather than report a channel missing that may be
/// present under a name nobody here can predict.</para></summary>
public enum MemorySeedKind
{
    /// <summary>Undeclared. A channel that has not said what it is; diagnostics treat it as possibly
    /// anything and stay quiet.</summary>
    Custom = 0,

    /// <summary>Matches the query's TEXT against stored text.</summary>
    Lexical = 1,

    /// <summary>Matches by embedding similarity, reading the vector store every write pays into.</summary>
    Semantic = 2,

    /// <summary>Fetches by a recorded SUBJECT handle rather than by scoring the query against content.
    /// </summary>
    Subject = 3,
}

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
/// <para><b>THE RETURN CONTRACT, in two parts. Eligibility first:</b> only a node reporting
/// <see cref="GraphNode.Matched"/> <c>true</c> can be ranked. <c>false</c> says the query did not match it;
/// <b><c>null</c> says "I have no relevance opinion about this node"</b> — right for a channel that fetched
/// by id or by handle rather than by scoring, and the ONLY way to stay unranked at every cardinality, since
/// a single eligible node is a source's top hit and takes rank 1.
/// <b>Then the gradient:</b> the eligible are ranked by this source's OWN
/// <see cref="GraphNode.Relevance"/>, never by list POSITION — higher is better, equal values share a rank,
/// the next distinct value skips the tied group's width. <b>Eligible nodes ALL on one value means "I have no
/// ordering", not "everything is maximally relevant"</b>, and the source contributes nothing; return a flat
/// value when that is the truth about your channel rather than fabricating a gradient.
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

    /// <summary>What ROLE this channel plays, for the wiring diagnostics that need to know whether an index
    /// a deployment is PAYING for can be reached by a recall.
    /// <para><b>Defaulted to <see cref="MemorySeedKind.Custom"/>, so an existing source compiles
    /// unchanged</b> — and a `Custom` channel makes those diagnostics ABSTAIN rather than accuse, because
    /// it may be the very channel they would report missing. Declare a kind to be counted; the cost of not
    /// declaring is silence, never a false finding.</para>
    /// <para>It is a ROLE, not an identity: several sources may share a kind, and the kind is not a key —
    /// <see cref="Name"/> stays the one thing that must be unique.</para></summary>
    MemorySeedKind Kind => MemorySeedKind.Custom;

    /// <summary>This channel's candidates, each carrying its own <see cref="GraphNode.Relevance"/> — see the
    /// type's remarks for what the engine does with that value and what a flat one means.</summary>
    /// <param name="request">What is being asked for.</param>
    /// <param name="ct">Cancellation.</param>
    /// <returns>Empty when this source has nothing to contribute. List ORDER is not read; the relevance
    /// gradient is.</returns>
    Task<IReadOnlyList<GraphNode>> SeedAsync(MemorySeedRequest request, CancellationToken ct);
}
