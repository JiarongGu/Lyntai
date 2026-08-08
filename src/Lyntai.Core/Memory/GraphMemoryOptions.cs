namespace Lyntai.Memory;

/// <summary>How the graph engine retrieves. Every value is defaulted; several are <b>unmeasured</b> and say
/// so — see the MEM-TUNE task before treating them as tuned.</summary>
public sealed record GraphMemoryOptions
{
    /// <summary>How far to spread from the seed set. Three or more hops reaches most of a connected graph,
    /// which defeats the purpose. Reasoned, not measured.</summary>
    public int Hops { get; init; } = 2;

    /// <summary>Rank attenuation per hop: material one hop out is worth this fraction of a direct match.
    /// Halving keeps hop-2 material below hop-1. Reasoned, not measured.</summary>
    public double HopAttenuation { get; init; } = 0.5;

    /// <summary>Associative material below this retrievability is dropped — the point at which something
    /// counts as forgotten, and the same threshold <c>PruneAsync</c> reaps by. Authoritative material holds
    /// 1.0 and is never affected. Chosen against the corpus in <c>MemoryDecaySimulationTests</c>, which pins the DYNAMICS — reuse
    /// outrunning interference — and not against production usage. A starting point, not a tuned value.</summary>
    public double MinRetrievability { get; init; } = 0.05;

    /// <summary>Length cap for a DERIVED headline; an authored one is used as given, and authoritative
    /// content is never shortened at all. Chosen against the corpus in <c>MemoryDecaySimulationTests</c>, which pins the DYNAMICS — reuse
    /// outrunning interference — and not against production usage. A starting point, not a tuned value.</summary>
    public int HeadlineChars { get; init; } = 120;

    /// <summary>How many of the returned nodes get co-activation edges. A ten-item recall would otherwise
    /// write forty-five edges every turn. Reasoned, not measured.</summary>
    public int CoActivationCap { get; init; } = 5;

    /// <summary>How many candidates to fetch per requested item. The store bounds the candidate set with
    /// plain arithmetic and the policy ranks it exactly afterwards, so a multiple above 1 is what keeps
    /// that ranking meaningful.</summary>
    public int CandidateMultiplier { get; init; } = 4;

    /// <summary>Items returned when the query names no limit.</summary>
    public int DefaultLimit { get; init; } = 10;

    /// <summary>How many near neighbours a new entry is linked to when similarity enrichment is wired (an
    /// <see cref="Lyntai.Embeddings.IEmbedder"/> and an <see cref="IVectorStore"/> are registered).
    /// Chosen against the corpus in <c>MemoryDecaySimulationTests</c>, which pins the DYNAMICS — reuse
    /// outrunning interference — and not against production usage. A starting point, not a tuned value.</summary>
    public int SimilarityK { get; init; } = 5;

    /// <summary>Cosine similarity below which enrichment does not link. Without a floor a new entry links
    /// to its <see cref="SimilarityK"/> nearest neighbours however unrelated they are, which in a small or
    /// young graph means linking to nearly everything. Chosen against the corpus in <c>MemoryDecaySimulationTests</c>, which pins the DYNAMICS — reuse
    /// outrunning interference — and not against production usage. A starting point, not a tuned value.</summary>
    public double MinSimilarity { get; init; } = 0.6;

    /// <summary>Constants of the default decay curve. Ignored when a custom
    /// <see cref="IRetrievabilityPolicy"/> is supplied.</summary>
    public HalfLifeOptions Decay { get; init; } = new();
}
