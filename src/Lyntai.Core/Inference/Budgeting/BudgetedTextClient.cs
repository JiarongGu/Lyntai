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
/// <para>The soft check-and-record is NOT atomic across a call, so under concurrency the cap can overshoot by
/// the combined cost of the calls in flight when it is crossed, not "one call past the cap". For a hard ceiling
/// set <see cref="BudgetOptions.ReserveCostUsd"/> (or <see cref="BudgetOptions.ReserveTokens"/>) over a tracker
/// that can reserve (<see cref="IReservingUsageTracker"/>): each call then holds that amount atomically before it
/// runs and reconciles to what it reported afterwards, released even when it throws or is cancelled.</para>
/// </summary>
public sealed class BudgetedTextClient(
    ITextClient inner, IUsageTracker tracker, LyntaiOptions options, ILogger<BudgetedTextClient>? logger = null) : DelegatingTextClient(inner)
{
    private readonly ILogger _logger = logger ?? NullLogger<BudgetedTextClient>.Instance;

    public override async Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default)
    {
        var admission = await AdmitAsync(req.Consumer, ct).ConfigureAwait(false);
        if (admission.Refusal is { } reason)
            return new TextResponse("", ProviderVerdict.Refused, Detail: reason);

        try
        {
            var reply = await Inner.CompleteAsync(req, ct).ConfigureAwait(false);
            await admission.SettleAsync(reply.Usage?.ToProviderUsage(), ct).ConfigureAwait(false);
            return reply;
        }
        finally
        {
            await admission.SettleAsync(null, ct).ConfigureAwait(false);
        }
    }

    public override async IAsyncEnumerable<TextChunk> StreamAsync(
        TextRequest req, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var admission = await AdmitAsync(req.Consumer, ct).ConfigureAwait(false);
        if (admission.Refusal is { } reason)
        {
            yield return TextChunk.Error(ProviderVerdict.Refused, reason);
            yield break;
        }

        try
        {
            await foreach (var chunk in Inner.StreamAsync(req, ct).ConfigureAwait(false))
            {
                if (chunk is { Kind: TextChunkKind.Final, Usage: not null })
                    await admission.SettleAsync(chunk.Usage.ToProviderUsage(), ct).ConfigureAwait(false);
                yield return chunk;
            }
        }
        finally
        {
            await admission.SettleAsync(null, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Admit or refuse a call for <paramref name="consumer"/> — delegated to the ONE
    /// <see cref="BudgetGate"/> the media router and the generic router's governance share, with token caps
    /// binding because a text call is token-metered.</summary>
    private ValueTask<BudgetAdmission> AdmitAsync(string consumer, CancellationToken ct) =>
        BudgetGate.AdmitAsync(options.Budget, tracker, consumer, includeTokens: true, _logger, ct);
}
