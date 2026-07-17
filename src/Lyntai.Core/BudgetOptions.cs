namespace Lyntai;

/// <summary>Spend/token caps for the opt-in usage budget (<c>AddUsageBudget</c>). A cap of null is
/// unlimited. Enforcement is a SOFT ceiling: the applicable accumulated total is checked before each call,
/// so the call that crosses the line still runs (its cost isn't known until it returns) and the next one is
/// refused. Read at runtime, so <c>LYNTAI_BUDGET_*</c> env overrides applied after configuration take
/// effect.</summary>
public sealed class BudgetOptions
{
    /// <summary>Global spend ceiling in USD across all consumers (sums each call's <c>Usage.CostUsd</c>).
    /// Null = unlimited.</summary>
    public double? MaxCostUsd { get; set; }

    /// <summary>Global token ceiling (input + output) across all consumers. Null = unlimited.</summary>
    public long? MaxTokens { get; set; }

    /// <summary>Per-consumer caps checked against that consumer's own running total, in addition to the
    /// global caps. A consumer absent from the map is bound only by the global caps.</summary>
    public Dictionary<string, ConsumerBudget> PerConsumer { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A per-consumer cap (see <see cref="BudgetOptions.PerConsumer"/>). Null fields fall back to
/// unlimited for that dimension (the global cap still applies).</summary>
public sealed record ConsumerBudget(double? MaxCostUsd = null, long? MaxTokens = null);
