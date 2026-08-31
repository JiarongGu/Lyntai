namespace Lyntai.Memory.Seeding;

/// <summary>Constants for <see cref="SemanticSeedSource"/>. An init-only record taken BY VALUE, matching
/// <see cref="Lyntai.Memory.GraphMemoryOptions"/> / <see cref="Lyntai.Memory.Forgetting.DsrOptions"/> /
/// <see cref="Lyntai.Memory.Ranking.ReciprocalRankFusionOptions"/> — the memory subsystem's own convention —
/// rather than an <c>Action&lt;T&gt;</c> configure callback: an init-only record cannot be mutated by one, so
/// a callback would silently do nothing.</summary>
public sealed record SemanticSeedOptions
{
    private readonly int _k = 20;

    /// <summary>How many semantically-similar entries this source considers per collection searched — a
    /// per-source SEARCH bound independent of <see cref="MemorySeedRequest.Limit"/>, which separately caps
    /// what this source RETURNS (see <see cref="SemanticSeedSource"/>'s own remarks on why the two stay
    /// apart). Reasoned, not measured — the mechanism is what registering this source establishes; the
    /// constant is a starting point, carrying the same "reasoned, not measured" status as
    /// <c>GraphMemoryOptions.SimilarityK</c> (not its value — that default is 5).
    /// <para><b>Must be positive.</b> Zero or negative stops this source from ever searching, which is
    /// indistinguishable from an outage — the seam already has a way to opt out of this source entirely:
    /// simply do not register it (see <see cref="Lyntai.LyntaiBuilder"/>'s <c>AddMemorySemanticSeeds</c>).</para></summary>
    /// <exception cref="ArgumentOutOfRangeException">Set to zero, a negative value, or a non-finite
    /// value.</exception>
    public int K
    {
        get => _k;
        init => _k = (int)MemoryOption.Require(value, MemoryOptionRange.Positive, nameof(SemanticSeedOptions),
            "a non-positive bound stops this source from ever searching, which is indistinguishable from an "
            + "outage; the way to opt out is to not register the source at all.");
    }
}
