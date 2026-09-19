using System.Globalization;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Lyntai.Inference.Budgeting;

/// <summary>The one budget check: the applicable accumulated totals against the configured caps, global
/// then per-consumer, reading a total only when a cap needs it. Shared by the text door, the media router
/// and the generic router's governance — it was two private copies once, and the third consumer is what
/// forced the extraction (the D77 rule: share the engine-independent correctness logic).</summary>
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

    private static string Refuse(string label, double cap, ILogger logger)
    {
        var reason = $"{label} of {cap.ToString(CultureInfo.InvariantCulture)} reached";
        logger.LogInformation("usage budget refusal: {Reason}", reason);
        LyntaiDiagnostics.RecordBudgetRefusal(label);
        return reason;
    }
}
