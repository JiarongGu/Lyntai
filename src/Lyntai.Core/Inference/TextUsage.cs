namespace Lyntai.Inference;

/// <summary>Token/cost accounting for one call, as reported by the provider.</summary>
public sealed record TextUsage(long InputTokens, long OutputTokens, long CacheReadTokens = 0, double? CostUsd = null)
{
    /// <summary>This call's spend in the ledger's shape-neutral currency (<see cref="ProviderUsage"/>).
    /// Cache reads are a text-shape detail the ledger does not count.</summary>
    public ProviderUsage ToProviderUsage() => new(InputTokens, OutputTokens, CostUsd);

    /// <summary>Two spends added: null when neither reported one, so "unmetered" never reads as "free"; a cost
    /// sums when either side reported one, and stays null when neither did.</summary>
    internal static TextUsage? Sum(TextUsage? first, TextUsage? second) =>
        first is null ? second
        : second is null ? first
        : new TextUsage(first.InputTokens + second.InputTokens, first.OutputTokens + second.OutputTokens,
            first.CacheReadTokens + second.CacheReadTokens,
            first.CostUsd is null && second.CostUsd is null ? null : (first.CostUsd ?? 0) + (second.CostUsd ?? 0));
}
