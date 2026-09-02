using Lyntai.Memory;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Verification;

namespace Lyntai.Benchmarks;

/// <summary>One benchmark ARM, as pure configuration.
/// <para><see cref="SemanticK"/> is the vector channel's WIDTH rather than a constructed
/// <c>IMemorySeedSource</c>, and <c>null</c> means "leave it unregistered". It cannot be a prebuilt source
/// because the vector store a source reads is created per arm by the bench.</para></summary>
internal sealed record FieldArm(
    string Name,
    GraphMemoryOptions? Options,
    IMemoryRankingPolicy? Ranking,
    IMemoryVerificationPolicy? Verification,
    int? SemanticK);

/// <summary>The arms BOTH field benchmarks share, so an arm name denotes one configuration on each.
///
/// <para><b>Why this is shared rather than copied.</b> The whole use of these arms is comparing a
/// configuration ACROSS the two workloads — LoCoMo rewards a perfect archive and LongMemEval's
/// knowledge-update class rewards suppressing a superseded fact — and a name meaning two things makes that
/// table silently wrong. It is the reasoning <c>CLAUDE.md</c> records for the two <c>--ranks</c> ladders
/// sharing one scoring replica so <c>K = 60</c> cannot mean two things.</para>
///
/// <para><b>What does NOT belong here: an arm that cannot be written down as constants.</b>
/// <c>+forget0+oracle</c>'s verifier is built from a conversation's own questions and <c>+forget0+judge</c>
/// needs a chat client, so both stay local to the bench that owns them. So do <c>+fuse</c> and
/// <c>+sem+fuse</c>: they are a matched pair from one fusion experiment, and <c>+fuse</c> additionally needs
/// bench-side seed wiring (the lexical channel ALONE) that no field here expresses.</para>
///
/// <para><b>Every member returns a FRESH instance.</b> Callers previously built these policies per
/// conversation, and handing back a cached one would share a policy object across engines — a semantic
/// change smuggled into a refactor whose control is that the published table reproduces exactly.</para>
/// </summary>
internal static class FieldArms
{
    /// <summary>What <c>+sem</c> means: <c>SemanticSeedOptions.K</c>'s SHIPPED default, stated as a number
    /// rather than taken from a bench's recall limit.
    ///
    /// <para><b>This is load-bearing, not tidiness.</b> The LoCoMo bench recalls 20 and the LongMemEval
    /// bench recalls 10, so defining the arm as "K = the recall limit" would give <c>+sem</c> two meanings
    /// in the one table built to compare them. Pinning it to the shipped default also makes the arm the
    /// configuration a deployment actually gets from <c>AddMemorySemanticSeeds()</c>, which is what the
    /// defaults question is about.</para></summary>
    internal const int ShippedSemanticK = 20;

    /// <summary>The wide semantic channel, kept explicit for the same reason
    /// <see cref="ShippedSemanticK"/> is.</summary>
    internal const int WideSemanticK = 80;

    /// <summary>Every shared arm, in ladder order. A fresh array of fresh arms on every call.</summary>
    internal static FieldArm[] All() =>
    [
        Shipped(),
        Named("+sem"),
        Named("+sem+hop0"),
        Named("+sem80"),
        Named("+sem80+hop0"),
        Named("+forget0"),
        Named("+sem+rel-only"),
        Named("+sem+mult"),
        Named("+sem80+mult"),
        Named("+rel-only"),
        Named("+sem5"),
        Named("+sem+forget2"),
    ];

    /// <summary>The SHIPPED defaults — no options, no ranking policy, no semantic channel. The control both
    /// benches quote, so it is named rather than spelled as four nulls at each call site.</summary>
    internal static FieldArm Shipped() => new("lyntai", null, null, null, null);

    /// <summary>One arm by name. Throws on an unknown name: a typo must not resolve to a silent default and
    /// then be reported in a table as if it had been measured.</summary>
    /// <exception cref="KeyNotFoundException">The name is not a shared arm.</exception>
    internal static FieldArm Named(string name) => name switch
    {
        "lyntai" => Shipped(),
        "+sem" => new(name, null, null, null, ShippedSemanticK),
        "+sem80" => new(name, null, null, null, WideSemanticK),

        // Traversal's vote off, semantic channel on.
        "+sem+hop0" => new(name, null, Fusion(hop: 0), null, ShippedSemanticK),
        "+sem80+hop0" => new(name, null, Fusion(hop: 0), null, WideSemanticK),

        // Forgetting's vote off. LoCoMo asks about months of history uniformly, so it penalises decay by
        // construction — which is exactly why this arm has to be priced on the OTHER workload before any
        // default moves.
        "+forget0" => new(name, null, Fusion(retrievability: 0), null, null),

        // Relevance alone. `+sem+rel-only` is the best mechanical arm measured on LoCoMo (82.6% at full
        // sample, above plain cosine's 81.1%); `+rel-only` is the same ordering with no semantic channel.
        "+sem+rel-only" => new(name, null, Fusion(retrievability: 0, hop: 0), null, ShippedSemanticK),
        "+rel-only" => new(name, null, Fusion(retrievability: 0, hop: 0), null, null),

        // Magnitude preserved instead of ranked by competition. Still ranks on the pooled
        // `GraphNode.Relevance`, so the mixed-scale defect per-source fusion removed survives here (D103).
        "+sem+mult" => new(name, null, new MultiplicativeRankingPolicy(), null, ShippedSemanticK),
        "+sem80+mult" => new(name, null, new MultiplicativeRankingPolicy(), null, WideSemanticK),

        // The BOTH-WORKLOADS pair (2026-09-03), and they separate two mechanisms the 2026-09-02 ladder
        // showed are distinct: seeding decides what is IN THE POOL, `RetrievabilityWeight` decides what gets
        // BURIED. `+sem` buys +22.0 points of LoCoMo search for -13.9 of supersession because it puts the
        // superseded fact in the pool while leaving forgetting's vote at its shipped strength.
        //   `+sem5`         narrows the pool — does a smaller semantic channel surface the stale fact less?
        //   `+sem+forget2`  leaves the pool wide and DOUBLES the vote that buries.
        // If the mechanisms are as separable as that table implies, the second recovers suppression while
        // keeping the search gain and the first does not, because a near-identical superseded fact is a TOP
        // semantic match at any K.
        "+sem5" => new(name, null, null, null, 5),
        "+sem+forget2" => new(name, null, Fusion(retrievability: 2), null, ShippedSemanticK),

        _ => throw new KeyNotFoundException($"'{name}' is not a shared field-benchmark arm. "
            + $"Shared arms: {string.Join(", ", All().Select(a => a.Name))}."),
    };

    /// <summary>Whether a name is a shared arm, for a bench merging these with arms of its own.</summary>
    internal static bool Has(string name) => All().Any(a => a.Name == name);

    private static ReciprocalRankFusionPolicy Fusion(double? retrievability = null, double? hop = null)
    {
        var options = new ReciprocalRankFusionOptions();
        if (retrievability is { } r) options = options with { RetrievabilityWeight = r };
        if (hop is { } h) options = options with { HopWeight = h };
        return new ReciprocalRankFusionPolicy(options);
    }
}
