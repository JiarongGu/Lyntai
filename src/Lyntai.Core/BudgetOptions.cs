namespace Lyntai;

/// <summary>Spend/token caps for the opt-in usage budget (<c>AddUsageBudget</c>). A cap of null is
/// unlimited. Enforcement is a SOFT ceiling: the applicable accumulated total is checked before each call,
/// so the call that crosses the line still runs (its cost isn't known until it returns) and the next one is
/// refused — unless <see cref="ReserveCostUsd"/> or <see cref="ReserveTokens"/> makes it hard. Read at
/// runtime, so <c>LYNTAI_BUDGET_*</c> env overrides applied after configuration take effect.</summary>
public sealed class BudgetOptions
{
    /// <summary>Global spend ceiling in USD across all consumers (sums each call's <c>Usage.CostUsd</c>).
    /// Null = unlimited.</summary>
    public double? MaxCostUsd { get; set; }

    /// <summary>Global token ceiling (input + output) across all consumers. Null = unlimited.</summary>
    public long? MaxTokens { get; set; }

    /// <summary>The cost HELD against the cost caps while one call runs, which makes them hard caps when the
    /// registered tracker can reserve (<see cref="Inference.Budgeting.IReservingUsageTracker"/>; every shipped
    /// tracker can): a call is admitted only if committed spend plus every in-flight hold plus this one fits, and
    /// afterwards the hold is replaced by what the call reported, however it ended. Set it to the most one call
    /// can cost — a call that spends more overshoots by the difference. Null, with <see cref="ReserveTokens"/> also
    /// null, keeps the soft check, as does a tracker that cannot reserve.</summary>
    public double? ReserveCostUsd { get; set; }

    /// <summary>The tokens (input + output) HELD against the token caps while one call runs — the token half of
    /// <see cref="ReserveCostUsd"/>, on the same terms. A render holds none: token caps never bind one.</summary>
    public long? ReserveTokens { get; set; }

    /// <summary>How long a hold counts if its call never reconciles — a process that died mid-call. Past it the
    /// hold stops counting, so it bounds what a crash can strand rather than the length of a call: a call that
    /// outlives it is still reconciled, but its budget was free for others meanwhile. Default 15 minutes.</summary>
    public TimeSpan ReservationLifetime { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Per-consumer caps checked against that consumer's own running total, in addition to the
    /// global caps. A consumer absent from the map is bound only by the global caps. Consumer identity is
    /// case-INSENSITIVE end-to-end: this lookup, the rate limiter's buckets, and the usage trackers'
    /// per-consumer totals all fold case, so "Chat"/"chat" share one cap and one total.</summary>
    public Dictionary<string, ConsumerBudget> PerConsumer { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>A per-consumer cap (see <see cref="BudgetOptions.PerConsumer"/>). Null fields fall back to
/// unlimited for that dimension (the global cap still applies).</summary>
public sealed record ConsumerBudget(double? MaxCostUsd = null, long? MaxTokens = null);
