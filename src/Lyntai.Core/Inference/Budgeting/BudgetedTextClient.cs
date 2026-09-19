using Lyntai.Inference;
using System.Globalization;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference.Budgeting;

/// <summary>
/// Decorates the front door with token/cost governance: before each call it checks the applicable
/// accumulated total against the configured caps and, if a cap is reached, REFUSES without hitting a
/// provider (a <see cref="ProviderVerdict.Refused"/> reply / an Error stream chunk). After a call it records the
/// reported usage. Wired by <c>AddUsageBudget()</c>. The ceiling is soft — the call that crosses a cap
/// still runs (its cost isn't known until it returns); the next one is refused.
/// <para>The check-and-record is deliberately NOT atomic across a call, so under concurrency the cap can
/// overshoot: every request already in flight when the cap is crossed passed its pre-call check and still
/// runs. The overshoot is bounded by the number of concurrent in-flight calls (their combined cost), not
/// "one call past the cap". If you need a hard ceiling, cap concurrency upstream or reserve-then-reconcile
/// in a custom <see cref="IUsageTracker"/>.</para>
/// </summary>
public sealed class BudgetedTextClient(
    ITextClient inner, IUsageTracker tracker, LyntaiOptions options, ILogger<BudgetedTextClient>? logger = null) : DelegatingTextClient(inner)
{
    private readonly ILogger _logger = logger ?? NullLogger<BudgetedTextClient>.Instance;

    public override async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        if (await OverBudgetAsync(req.Consumer, ct).ConfigureAwait(false) is { } reason)
            return new TextResponse("", ProviderVerdict.Refused, Detail: reason);

        var reply = await Inner.CompleteAsync(req, ct).ConfigureAwait(false);
        if (reply.Usage is not null) await tracker.RecordAsync(req.Consumer, reply.Usage.ToProviderUsage(), ct).ConfigureAwait(false);
        return reply;
    }

    public override async IAsyncEnumerable<TextChunk> StreamAsync(
        TextRequest req, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        if (await OverBudgetAsync(req.Consumer, ct).ConfigureAwait(false) is { } reason)
        {
            yield return TextChunk.Error(ProviderVerdict.Refused, reason);
            yield break;
        }

        await foreach (var chunk in Inner.StreamAsync(req, ct).ConfigureAwait(false))
        {
            if (chunk is { Kind: TextChunkKind.Final, Usage: not null })
                await tracker.RecordAsync(req.Consumer, chunk.Usage.ToProviderUsage(), ct).ConfigureAwait(false);
            yield return chunk;
        }
    }

    /// <summary>The refusal reason when a cap that applies to <paramref name="consumer"/> has been reached
    /// — delegated to the ONE <see cref="BudgetGate"/> the media router and the generic router's
    /// governance share, with token caps binding because a text call is token-metered.</summary>
    private ValueTask<string?> OverBudgetAsync(string consumer, CancellationToken ct) =>
        BudgetGate.OverBudgetAsync(options.Budget, tracker, consumer, includeTokens: true, _logger, ct);
}
