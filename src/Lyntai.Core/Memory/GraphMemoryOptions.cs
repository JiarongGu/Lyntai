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
    /// 1.0 and is never affected. <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
    public double MinRetrievability { get; init; } = 0.05;

    /// <summary>Length cap for a DERIVED headline; an authored one is used as given, and authoritative
    /// content is never shortened at all. <b>Unmeasured</b> — see the MEM-TUNE task.</summary>
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

    /// <summary>Constants of the default decay curve. Ignored when a custom
    /// <see cref="IRetrievabilityPolicy"/> is supplied.</summary>
    public HalfLifeOptions Decay { get; init; } = new();
}
