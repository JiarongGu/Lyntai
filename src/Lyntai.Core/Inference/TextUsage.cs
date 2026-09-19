namespace Lyntai.Inference;

/// <summary>Token/cost accounting for one call, as reported by the provider.</summary>
public sealed record TextUsage(long InputTokens, long OutputTokens, long CacheReadTokens = 0, double? CostUsd = null)
{
    /// <summary>This call's spend in the ledger's shape-neutral currency (<see cref="ProviderUsage"/>).
    /// Cache reads are a text-shape detail the ledger does not count.</summary>
    public ProviderUsage ToProviderUsage() => new(InputTokens, OutputTokens, CostUsd);
}
