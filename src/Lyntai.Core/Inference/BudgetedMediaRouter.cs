using System.Globalization;
using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;
using Lyntai.Inference.Budgeting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference;

/// <summary>
/// Wraps generation routing in spend governance: before a render it checks the applicable accumulated cost
/// against the configured caps and, if one is reached, REFUSES without calling a backend; after a render it
/// records what the backend reported costing. Wired by <c>AddMediaUsageBudget()</c>.
///
/// <para>It records into the SAME <see cref="IUsageTracker"/> the text front door (<see cref="ITextClient"/>) uses, on purpose: "what has
/// this app spent" has to be one number, and a host that pays one vendor for both chat and images would
/// otherwise have to add up two. A consequence worth knowing: renders and chat share the global cost cap, so
/// an expensive render can refuse a subsequent chat call and vice versa. That is the intent — it's one
/// wallet — and per-consumer caps are how you fence a specific workload off.</para>
///
/// <para>Only COST caps bind a render. A render spends no tokens (it reports none, and the tracker is told
/// none), so refusing one because chat exhausted a token budget would be governance by coincidence. Set
/// <see cref="BudgetOptions.MaxCostUsd"/> — or a per-consumer cost cap — to bound generation.</para>
///
/// <para>The ceiling is soft, exactly as on the LLM side: the call that crosses a cap still runs (its cost
/// isn't known until it returns) and the next one is refused. Under concurrency the overshoot is bounded by
/// the combined cost of the calls already in flight when the cap was crossed.</para>
/// </summary>
/// <param name="inner">The router being governed.</param>
/// <param name="tracker">Shared spend ledger — the text front door's tracker.</param>
/// <param name="options">Where the caps live (<see cref="LyntaiOptions.Budget"/>).</param>
/// <param name="logger">Optional; one line per refusal.</param>
public sealed class BudgetedMediaRouter(
    IMediaRouter inner,
    IUsageTracker tracker,
    LyntaiOptions options,
    ILogger<BudgetedMediaRouter>? logger = null) : IMediaRouter
{
    private readonly ILogger _logger = logger ?? NullLogger<BudgetedMediaRouter>.Instance;

    /// <inheritdoc/>
    public async Task<MediaResponse> GenerateAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        if (await OverBudgetAsync(request.Consumer, ct).ConfigureAwait(false) is { } reason)
            return MediaResponse.Failure(ProviderVerdict.Refused, reason);

        var result = await inner.GenerateAsync(candidates, request, ct).ConfigureAwait(false);
        await RecordAsync(request.Consumer, result.Usage, ct).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc/>
    /// <remarks>The submit path is where the money is COMMITTED — a hosted video render is charged for
    /// whether or not anyone ever fetches it — so the check belongs here rather than at fetch time. The cost
    /// itself is only known when the render finishes, which is why whatever FETCHES it records it — the durable
    /// job handlers and the <c>generate_fetch</c> tool: this decorator never sees the completed result.</remarks>
    public async Task<MediaSubmission> SubmitAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        if (await OverBudgetAsync(request.Consumer, ct).ConfigureAwait(false) is { } reason)
            return MediaSubmission.Failure(ProviderVerdict.Refused, reason); // the inline door's verdict

        return await inner.SubmitAsync(candidates, request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>Governed on the SAME terms as the other two doors, and it has to be: a decorator that passed
    /// one door straight through would make streaming the cheapest way to spend past a cap, which is the
    /// <c>pitfalls.md</c> § "Second doors" shape — a capability enforced at one entry point is not enforced
    /// when a second entry point reaches the same objects.
    /// <para>The refusal is delivered as a terminal <see cref="MediaChunk.Failure"/> rather than a
    /// thrown exception, because a caller writing <c>await foreach</c> should learn about a budget refusal
    /// the same way they learn about a backend refusal.</para>
    /// <para>Cost is recorded from the terminal chunk's <see cref="MediaChunk.Usage"/>, which is the
    /// only place a streaming backend can report it — the total is not known until the stream ends. A backend
    /// that reports none records none, exactly as on the inline path.</para></remarks>
    public async IAsyncEnumerable<MediaChunk> StreamAsync(
        IReadOnlyList<ProviderCandidate> candidates, MediaRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (await OverBudgetAsync(request.Consumer, ct).ConfigureAwait(false) is { } reason)
        {
            yield return MediaChunk.Failure(ProviderVerdict.Refused, reason);
            yield break;
        }

        await foreach (var chunk in inner.StreamAsync(candidates, request, ct).ConfigureAwait(false))
        {
            if (chunk.Usage is not null)
                await RecordAsync(request.Consumer, chunk.Usage, ct).ConfigureAwait(false);
            yield return chunk;
        }
    }

    private ValueTask RecordAsync(string consumer, MediaUsage? usage, CancellationToken ct) =>
        BudgetGate.RecordCostAsync(tracker, consumer, usage, ct);

    /// <summary>The refusal reason when a COST cap that applies to <paramref name="consumer"/> has been
    /// reached — delegated to the ONE <see cref="BudgetGate"/> the text door and the generic
    /// router's governance share, with token caps EXCLUDED: a render spends no tokens, and refusing one
    /// because chat exhausted a token budget would be governance by coincidence.</summary>
    private ValueTask<string?> OverBudgetAsync(string consumer, CancellationToken ct) =>
        BudgetGate.OverBudgetAsync(options.Budget, tracker, consumer, includeTokens: false, _logger, ct);
}
