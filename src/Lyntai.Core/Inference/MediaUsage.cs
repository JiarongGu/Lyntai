namespace Lyntai.Inference;

/// <summary>What a generation cost, as far as the backend reported it. Every field is nullable because
/// backends report wildly different things (a count of images, seconds of video/audio, a price) and the
/// platform never INVENTS a cost from a token price.</summary>
/// <param name="Count">Number of artifacts billed.</param>
/// <param name="Seconds">Duration produced, for time-based media.</param>
/// <param name="CostUsd">What the backend said it cost.</param>
public sealed record MediaUsage(int? Count = null, double? Seconds = null, double? CostUsd = null)
{
    /// <summary>This render's spend in the ledger's shape-neutral currency (<see cref="ProviderUsage"/>) —
    /// the cost alone, because counts and seconds are not tokens and the platform never invents a price.</summary>
    public ProviderUsage ToProviderUsage() => new(CostUsd: CostUsd);
}
