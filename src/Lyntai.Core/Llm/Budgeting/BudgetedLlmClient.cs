using System.Globalization;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Llm.Budgeting;

/// <summary>
/// Decorates the front door with token/cost governance: before each call it checks the applicable
/// accumulated total against the configured caps and, if a cap is reached, REFUSES without hitting a
/// provider (a <see cref="LlmVerdict.Refused"/> reply / an Error stream chunk). After a call it records the
/// reported usage. Wired by <c>AddUsageBudget()</c>. The ceiling is soft — the call that crosses a cap
/// still runs (its cost isn't known until it returns); the next one is refused.
/// <para>The check-and-record is deliberately NOT atomic across a call, so under concurrency the cap can
/// overshoot: every request already in flight when the cap is crossed passed its pre-call check and still
/// runs. The overshoot is bounded by the number of concurrent in-flight calls (their combined cost), not
/// "one call past the cap". If you need a hard ceiling, cap concurrency upstream or reserve-then-reconcile
/// in a custom <see cref="IUsageTracker"/>.</para>
/// </summary>
public sealed class BudgetedLlmClient(
    ILlmClient inner, IUsageTracker tracker, LyntaiOptions options, ILogger<BudgetedLlmClient>? logger = null) : DelegatingLlmClient(inner)
{
    private readonly ILogger _logger = logger ?? NullLogger<BudgetedLlmClient>.Instance;

    public override async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        if (await OverBudgetAsync(req.Consumer, ct).ConfigureAwait(false) is { } reason)
            return new LlmReply("", LlmVerdict.Refused, Detail: reason);

        var reply = await Inner.CompleteAsync(req, ct).ConfigureAwait(false);
        if (reply.Usage is not null) await tracker.RecordAsync(req.Consumer, reply.Usage, ct).ConfigureAwait(false);
        return reply;
    }

    public override async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmRequest req, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (await OverBudgetAsync(req.Consumer, ct).ConfigureAwait(false) is { } reason)
        {
            yield return LlmChunk.Error(LlmVerdict.Refused, reason);
            yield break;
        }

        await foreach (var chunk in Inner.StreamAsync(req, ct).ConfigureAwait(false))
        {
            if (chunk is { Kind: LlmChunkKind.Final, Usage: not null })
                await tracker.RecordAsync(req.Consumer, chunk.Usage, ct).ConfigureAwait(false);
            yield return chunk;
        }
    }

    /// <summary>The refusal reason when a cap that applies to <paramref name="consumer"/> has been reached
    /// — the global caps (vs the global total) or the consumer's own caps (vs its total) — else null.</summary>
    private async ValueTask<string?> OverBudgetAsync(string consumer, CancellationToken ct)
    {
        var budget = options.Budget;
        var global = await tracker.TotalAsync(ct: ct).ConfigureAwait(false);
        if (budget.MaxCostUsd is { } gc && global.CostUsd >= gc) return Refuse("global cost budget", gc);
        if (budget.MaxTokens is { } gt && global.TotalTokens >= gt) return Refuse("global token budget", gt);

        if (budget.PerConsumer.TryGetValue(consumer, out var cb))
        {
            var mine = await tracker.TotalAsync(consumer, ct).ConfigureAwait(false);
            if (cb.MaxCostUsd is { } cc && mine.CostUsd >= cc) return Refuse($"consumer '{consumer}' cost budget", cc);
            if (cb.MaxTokens is { } cct && mine.TotalTokens >= cct) return Refuse($"consumer '{consumer}' token budget", cct);
        }

        return null;
    }

    private string Refuse(string label, double cap)
    {
        var reason = $"{label} of {cap.ToString(CultureInfo.InvariantCulture)} reached";
        _logger.LogInformation("usage budget refusal: {Reason}", reason);
        LyntaiDiagnostics.RecordBudgetRefusal(label);
        return reason;
    }
}
