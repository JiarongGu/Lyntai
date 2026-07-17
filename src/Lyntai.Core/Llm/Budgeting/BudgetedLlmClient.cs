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
/// </summary>
public sealed class BudgetedLlmClient(
    ILlmClient inner, IUsageTracker tracker, LyntaiOptions options, ILogger<BudgetedLlmClient>? logger = null) : ILlmClient
{
    private readonly ILogger _logger = logger ?? NullLogger<BudgetedLlmClient>.Instance;

    public async Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
    {
        if (IsOverBudget(req.Consumer, out var reason))
            return new LlmReply("", LlmVerdict.Refused, Detail: reason);

        var reply = await inner.CompleteAsync(req, ct).ConfigureAwait(false);
        if (reply.Usage is not null) tracker.Record(req.Consumer, reply.Usage);
        return reply;
    }

    public async IAsyncEnumerable<LlmChunk> StreamAsync(
        LlmRequest req, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (IsOverBudget(req.Consumer, out var reason))
        {
            yield return LlmChunk.Error(LlmVerdict.Refused, reason);
            yield break;
        }

        await foreach (var chunk in inner.StreamAsync(req, ct).ConfigureAwait(false))
        {
            if (chunk is { Kind: LlmChunkKind.Final, Usage: not null }) tracker.Record(req.Consumer, chunk.Usage);
            yield return chunk;
        }
    }

    public bool SupportsToolCalls(LlmRequest req) => inner.SupportsToolCalls(req);

    /// <summary>True when a cap that applies to <paramref name="consumer"/> has been reached: the global
    /// caps (vs the global total) or the consumer's own caps (vs its total).</summary>
    private bool IsOverBudget(string consumer, out string reason)
    {
        var budget = options.Budget;
        var global = tracker.Total();
        if (budget.MaxCostUsd is { } gc && global.CostUsd >= gc) { reason = Refuse("global cost budget", gc); return true; }
        if (budget.MaxTokens is { } gt && global.TotalTokens >= gt) { reason = Refuse("global token budget", gt); return true; }

        if (budget.PerConsumer.TryGetValue(consumer, out var cb))
        {
            var ct = tracker.Total(consumer);
            if (cb.MaxCostUsd is { } cc && ct.CostUsd >= cc) { reason = Refuse($"consumer '{consumer}' cost budget", cc); return true; }
            if (cb.MaxTokens is { } cct && ct.TotalTokens >= cct) { reason = Refuse($"consumer '{consumer}' token budget", cct); return true; }
        }

        reason = "";
        return false;
    }

    private string Refuse(string label, double cap)
    {
        var reason = $"{label} of {cap.ToString(CultureInfo.InvariantCulture)} reached";
        _logger.LogInformation("usage budget refusal: {Reason}", reason);
        LyntaiDiagnostics.RecordBudgetRefusal(label);
        return reason;
    }
}
