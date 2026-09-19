namespace Lyntai.Inference;

/// <summary>What one call SPENT, whatever shape it produced — the ledger's currency.
/// <see cref="Budgeting.IUsageTracker"/> records these across every kind (a completion, a render, an
/// embed), so the type is named for the PROVIDER rather than for any one call shape: a media call has no
/// tokens and a text call has no artifact count, but both spend, and the ledger must not be typed to
/// either's vocabulary. The per-shape reports stay richer on purpose (<see cref="TextUsage"/> carries
/// cache reads, <see cref="MediaUsage"/> counts and seconds); each projects into this via its
/// <c>ToProviderUsage()</c>.</summary>
/// <param name="InputTokens">Prompt-side tokens, where the shape has them; 0 where it does not.</param>
/// <param name="OutputTokens">Completion-side tokens, where the shape has them; 0 where it does not.</param>
/// <param name="CostUsd">What the backend said it cost, when it said anything.</param>
public sealed record ProviderUsage(long InputTokens = 0, long OutputTokens = 0, double? CostUsd = null);
