using System.Globalization;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Lyntai.Inference.Budgeting;

/// <summary>The one budget check: the applicable accumulated totals against the configured caps, global
/// then per-consumer, reading a total only when a cap needs it. Shared by the text door, the media router
/// and the generic router's governance (the D77 rule: share the engine-independent correctness logic).</summary>
internal static class BudgetGate
{
    /// <summary>The refusal reason when a cap that applies to <paramref name="consumer"/> has been reached,
    /// else null. The ceiling is soft — the call that crosses a cap still runs; the next one is refused.</summary>
    /// <param name="budget">The caps.</param>
    /// <param name="tracker">The ledger the totals are read from.</param>
    /// <param name="consumer">Whose bucket, beside the global one.</param>
    /// <param name="includeTokens">Whether token caps bind this kind. True for token-metered calls (text,
    /// vector, score); false for a render, which spends no tokens — refusing one because chat exhausted a
    /// token budget would be governance by coincidence (<c>BudgetedMediaRouter</c>'s own rule).</param>
    /// <param name="logger">One line per refusal.</param>
    /// <param name="ct">Caller cancellation; propagates.</param>
    internal static async ValueTask<string?> OverBudgetAsync(
        BudgetOptions budget, IUsageTracker tracker, string consumer, bool includeTokens,
        ILogger logger, CancellationToken ct)
    {
        if (budget.MaxCostUsd is not null || (includeTokens && budget.MaxTokens is not null))
        {
            var global = await tracker.TotalAsync(ct: ct).ConfigureAwait(false);
            if (budget.MaxCostUsd is { } gc && global.CostUsd >= gc)
                return Refuse("global cost budget", gc, logger);
            if (includeTokens && budget.MaxTokens is { } gt && global.TotalTokens >= gt)
                return Refuse("global token budget", gt, logger);
        }

        if (budget.PerConsumer.TryGetValue(consumer, out var cb)
            && (cb.MaxCostUsd is not null || (includeTokens && cb.MaxTokens is not null)))
        {
            var mine = await tracker.TotalAsync(consumer, ct).ConfigureAwait(false);
            if (cb.MaxCostUsd is { } cc && mine.CostUsd >= cc)
                return Refuse($"consumer '{consumer}' cost budget", cc, logger);
            if (includeTokens && cb.MaxTokens is { } cct && mine.TotalTokens >= cct)
                return Refuse($"consumer '{consumer}' token budget", cct, logger);
        }

        return null;
    }

    /// <summary>Admit one call: reserve when the tracker can (<see cref="IReservingUsageTracker"/>) and a hold is
    /// configured, else the soft <see cref="OverBudgetAsync"/> check. Settle the admission once the call ends —
    /// <see cref="BudgetAdmission.SettleAsync"/> in a <c>finally</c>, so a throw or a cancellation releases the hold.</summary>
    internal static async ValueTask<BudgetAdmission> AdmitAsync(
        BudgetOptions budget, IUsageTracker tracker, string consumer, bool includeTokens,
        ILogger logger, CancellationToken ct)
    {
        var holdTokens = includeTokens ? budget.ReserveTokens : null;
        if (tracker is not IReservingUsageTracker ledger || (budget.ReserveCostUsd is null && holdTokens is null))
            return new BudgetAdmission(
                await OverBudgetAsync(budget, tracker, consumer, includeTokens, logger, ct).ConfigureAwait(false),
                tracker, consumer, null, null);

        budget.PerConsumer.TryGetValue(consumer, out var cb);
        var caps = new UsageCaps(
            budget.MaxCostUsd, includeTokens ? budget.MaxTokens : null,
            cb?.MaxCostUsd, includeTokens ? cb?.MaxTokens : null);
        if (!caps.HasGlobal && !caps.HasConsumer)
            return new BudgetAdmission(null, tracker, consumer, null, null);

        var result = await ledger.ReserveAsync(new UsageReservationRequest(
            consumer, new ProviderUsage(holdTokens ?? 0, 0, budget.ReserveCostUsd), caps,
            DateTimeOffset.UtcNow + budget.ReservationLifetime), ct).ConfigureAwait(false);
        if (result.Reservation is { } held)
            return new BudgetAdmission(null, tracker, consumer, ledger, held);

        var reason = result.ExceededCap switch
        {
            UsageCap.GlobalTokens => Refuse("global token budget", caps.MaxTokens ?? 0, logger),
            UsageCap.ConsumerCost => Refuse($"consumer '{consumer}' cost budget", caps.ConsumerMaxCostUsd ?? 0, logger),
            UsageCap.ConsumerTokens => Refuse($"consumer '{consumer}' token budget", caps.ConsumerMaxTokens ?? 0, logger),
            _ => Refuse("global cost budget", caps.MaxCostUsd ?? 0, logger),
        };
        return new BudgetAdmission(reason, tracker, consumer, null, null);
    }

    /// <summary>Record what a render reported costing into the ledger, as a cost-only entry
    /// (<see cref="MediaUsage.ToProviderUsage"/>) — the one place generation spend maps onto the ledger. A render
    /// that reported no positive cost records nothing.</summary>
    internal static ValueTask RecordCostAsync(
        IUsageTracker tracker, string consumer, MediaUsage? usage, CancellationToken ct = default) =>
        usage is { CostUsd: > 0 }
            ? tracker.RecordAsync(consumer, usage.ToProviderUsage(), ct)
            : ValueTask.CompletedTask;

    private static string Refuse(string label, double cap, ILogger logger)
    {
        var reason = $"{label} of {cap.ToString(CultureInfo.InvariantCulture)} reached";
        logger.LogInformation("usage budget refusal: {Reason}", reason);
        LyntaiDiagnostics.RecordBudgetRefusal(label);
        return reason;
    }
}

/// <summary>One call's passage through <see cref="BudgetGate.AdmitAsync"/>: refused, admitted on the soft check, or
/// admitted on a hold that <see cref="SettleAsync"/> must reconcile.</summary>
internal sealed class BudgetAdmission(
    string? refusal, IUsageTracker tracker, string consumer, IReservingUsageTracker? ledger, UsageReservation? held)
{
    private int _settled;

    /// <summary>Why the call is refused, or null when it may run.</summary>
    public string? Refusal { get; } = refusal;

    /// <summary>Account for what the call spent. A hold is reconciled ONCE, with no cancellation — a cancelled call
    /// must still release it; anything after that, or on the soft path, is recorded as before. Null records
    /// nothing, so a <c>finally</c> calling this after the usage was settled is a no-op.</summary>
    public ValueTask SettleAsync(ProviderUsage? usage, CancellationToken ct)
    {
        if (held is not null && Interlocked.Exchange(ref _settled, 1) == 0)
            return ledger!.ReconcileAsync(held, usage, CancellationToken.None);
        return usage is null ? ValueTask.CompletedTask : tracker.RecordAsync(consumer, usage, ct);
    }
}
