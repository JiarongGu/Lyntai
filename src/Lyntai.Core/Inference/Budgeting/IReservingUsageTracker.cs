using Lyntai.Inference;
namespace Lyntai.Inference.Budgeting;

/// <summary>An OPTIONAL <see cref="IUsageTracker"/> capability that makes a cap HARD: before a call, hold an amount
/// against the caps, admitting it only if committed spend plus every live hold plus this one fits; after it,
/// reconcile — release the hold and record what the call actually spent. The budget decorators use it when the
/// registered tracker implements it AND <see cref="BudgetOptions.ReserveCostUsd"/> or
/// <see cref="BudgetOptions.ReserveTokens"/> is set; otherwise they keep the soft check. The cap is only as hard as
/// the hold is honest: a call that spends more than it held overshoots by the difference.
/// <para>An implementer must decide admission and write the hold in ONE atomic step, so concurrent reservations
/// against one cap admit only what fits (<see cref="UsageCaps.ExceededBy"/> is the rule); fold consumer identity
/// case-insensitively, as <see cref="IUsageTracker.TotalAsync"/> does; ignore a hold whose
/// <see cref="UsageReservation.ExpiresAt"/> has passed, so a caller that died cannot hold budget forever; and leave
/// holds alone on <see cref="IUsageTracker.ResetAsync"/>, which clears SPEND, not calls still in flight.</para></summary>
public interface IReservingUsageTracker : IUsageTracker
{
    /// <summary>Hold <see cref="UsageReservationRequest.Amount"/> under the request's consumer if it fits every cap
    /// the request names, atomically with the check.</summary>
    /// <returns>A granted result carrying the reservation, or a refused one naming the first cap that would not
    /// hold it.</returns>
    ValueTask<UsageReservationResult> ReserveAsync(UsageReservationRequest request, CancellationToken ct = default);

    /// <summary>Release <paramref name="reservation"/> and record <paramref name="actual"/> in its place, as one
    /// atomic step. Call it exactly once per reservation: an expired or already-released hold still records
    /// <paramref name="actual"/>.</summary>
    /// <param name="reservation">What <see cref="ReserveAsync"/> granted.</param>
    /// <param name="actual">What the call spent; null records nothing — it failed, was cancelled, or reported no
    /// usage.</param>
    /// <param name="ct">Cancellation. The budget decorators pass none, so a cancelled call still releases.</param>
    ValueTask ReconcileAsync(UsageReservation reservation, ProviderUsage? actual, CancellationToken ct = default);
}

/// <summary>What to hold, for whom, against which caps, and until when.</summary>
/// <param name="Consumer">Whose bucket, beside the global one.</param>
/// <param name="Amount">The most the call is expected to spend. A null cost holds nothing against cost caps.</param>
/// <param name="Caps">The caps the hold must fit under.</param>
/// <param name="ExpiresAt">When the hold stops counting, whether or not it was reconciled.</param>
public sealed record UsageReservationRequest(string Consumer, ProviderUsage Amount, UsageCaps Caps, DateTimeOffset ExpiresAt);

/// <summary>A granted hold, handed back to <see cref="IReservingUsageTracker.ReconcileAsync"/>.</summary>
/// <param name="Id">The tracker's identifier for the hold.</param>
/// <param name="Consumer">Whose bucket it is held in.</param>
/// <param name="Amount">What it holds.</param>
/// <param name="ExpiresAt">When it stops counting.</param>
public sealed record UsageReservation(string Id, string Consumer, ProviderUsage Amount, DateTimeOffset ExpiresAt);

/// <summary>The outcome of <see cref="IReservingUsageTracker.ReserveAsync"/>.</summary>
/// <param name="Reservation">The hold, when granted; null when refused.</param>
/// <param name="ExceededCap">The first cap that would not hold the amount, when refused.</param>
public sealed record UsageReservationResult(UsageReservation? Reservation, UsageCap? ExceededCap = null)
{
    /// <summary>Whether the hold was written.</summary>
    public bool Granted => Reservation is not null;
}

/// <summary>Which cap refused a reservation.</summary>
public enum UsageCap
{
    /// <summary><see cref="UsageCaps.MaxCostUsd"/>.</summary>
    GlobalCost,
    /// <summary><see cref="UsageCaps.MaxTokens"/>.</summary>
    GlobalTokens,
    /// <summary><see cref="UsageCaps.ConsumerMaxCostUsd"/>.</summary>
    ConsumerCost,
    /// <summary><see cref="UsageCaps.ConsumerMaxTokens"/>.</summary>
    ConsumerTokens,
}

/// <summary>The caps a reservation must fit under, global and for its consumer. Null is unlimited.</summary>
/// <param name="MaxCostUsd">Global cost cap, across every consumer.</param>
/// <param name="MaxTokens">Global token cap (input + output).</param>
/// <param name="ConsumerMaxCostUsd">The reservation's consumer's cost cap.</param>
/// <param name="ConsumerMaxTokens">The reservation's consumer's token cap.</param>
public sealed record UsageCaps(
    double? MaxCostUsd = null, long? MaxTokens = null, double? ConsumerMaxCostUsd = null, long? ConsumerMaxTokens = null)
{
    // Sums of REAL drift in the last bits: 0.1 + 0.2 must still fit a cap of 0.3.
    private const double CostTolerance = 1e-9;

    /// <summary>Whether a global cap is set, so the global load must be read.</summary>
    public bool HasGlobal => MaxCostUsd is not null || MaxTokens is not null;

    /// <summary>Whether a consumer cap is set, so the consumer's load must be read.</summary>
    public bool HasConsumer => ConsumerMaxCostUsd is not null || ConsumerMaxTokens is not null;

    /// <summary>The admission rule, the same for every tracker: a cap refuses when the LOAD — committed spend plus
    /// live holds — has already reached it, or when adding <paramref name="amount"/> would pass it. Checked global
    /// cost, global tokens, consumer cost, consumer tokens, in that order.</summary>
    /// <param name="globalLoad">Every consumer's spend plus every live hold; ignored without a global cap.</param>
    /// <param name="consumerLoad">The consumer's spend plus its live holds; ignored without a consumer cap.</param>
    /// <param name="amount">What is to be held.</param>
    /// <returns>The first cap that refuses, or null when the amount fits.</returns>
    public UsageCap? ExceededBy(UsageTotals globalLoad, UsageTotals consumerLoad, ProviderUsage amount)
    {
        ArgumentNullException.ThrowIfNull(globalLoad);
        ArgumentNullException.ThrowIfNull(consumerLoad);
        ArgumentNullException.ThrowIfNull(amount);
        var cost = amount.CostUsd ?? 0;
        var tokens = amount.InputTokens + amount.OutputTokens;
        if (Refuses(MaxCostUsd, globalLoad.CostUsd, cost)) return UsageCap.GlobalCost;
        if (Refuses(MaxTokens, globalLoad.TotalTokens, tokens)) return UsageCap.GlobalTokens;
        if (Refuses(ConsumerMaxCostUsd, consumerLoad.CostUsd, cost)) return UsageCap.ConsumerCost;
        if (Refuses(ConsumerMaxTokens, consumerLoad.TotalTokens, tokens)) return UsageCap.ConsumerTokens;
        return null;
    }

    private static bool Refuses(double? cap, double load, double amount) =>
        cap is { } c && (load >= c - CostTolerance || load + amount > c + CostTolerance);

    private static bool Refuses(long? cap, long load, long amount) =>
        cap is { } c && (load >= c || load + amount > c);
}
