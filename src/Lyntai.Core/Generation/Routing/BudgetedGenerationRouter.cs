using System.Globalization;
using System.Runtime.CompilerServices;
using Lyntai.Diagnostics;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Generation.Routing;

/// <summary>
/// Wraps generation routing in spend governance: before a render it checks the applicable accumulated cost
/// against the configured caps and, if one is reached, REFUSES without calling a backend; after a render it
/// records what the backend reported costing. Wired by <c>AddGenerationUsageBudget()</c>.
///
/// <para>It records into the SAME <see cref="IUsageTracker"/> the LLM front door uses, on purpose: "what has
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
/// <param name="tracker">Shared spend ledger — the LLM front door's tracker.</param>
/// <param name="options">Where the caps live (<see cref="LyntaiOptions.Budget"/>).</param>
/// <param name="logger">Optional; one line per refusal.</param>
public sealed class BudgetedGenerationRouter(
    IGenerationRouter inner,
    IUsageTracker tracker,
    LyntaiOptions options,
    ILogger<BudgetedGenerationRouter>? logger = null) : IGenerationRouter
{
    private readonly ILogger _logger = logger ?? NullLogger<BudgetedGenerationRouter>.Instance;

    /// <inheritdoc/>
    public async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        if (await OverBudgetAsync(request.Consumer, ct).ConfigureAwait(false) is { } reason)
            return GenerationResult.Failure(GenerationVerdict.Refused, reason);

        var result = await inner.GenerateAsync(candidates, request, ct).ConfigureAwait(false);
        await RecordAsync(request.Consumer, result.Usage, ct).ConfigureAwait(false);
        return result;
    }

    /// <inheritdoc/>
    /// <remarks>The submit path is where the money is COMMITTED — a hosted video render is charged for
    /// whether or not anyone ever fetches it — so the check belongs here rather than at fetch time. The cost
    /// itself is only known when the render finishes, which is why <c>GenerationRenderJobHandler</c> records
    /// it: this decorator never sees the completed result.</remarks>
    public async Task<GenerationSubmission> SubmitAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        if (await OverBudgetAsync(request.Consumer, ct).ConfigureAwait(false) is { } reason)
            return new GenerationSubmission("",
                new GenerationOperation("", GenerationOperationStatus.Failed, Detail: reason));

        return await inner.SubmitAsync(candidates, request, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    /// <remarks>Governed on the SAME terms as the other two doors, and it has to be: a decorator that passed
    /// one door straight through would make streaming the cheapest way to spend past a cap, which is the
    /// <c>pitfalls.md</c> § "Second doors" shape — a capability enforced at one entry point is not enforced
    /// when a second entry point reaches the same objects.
    /// <para>The refusal is delivered as a terminal <see cref="GenerationChunk.Failure"/> rather than a
    /// thrown exception, because a caller writing <c>await foreach</c> should learn about a budget refusal
    /// the same way they learn about a backend refusal.</para>
    /// <para>Cost is recorded from the terminal chunk's <see cref="GenerationChunk.Usage"/>, which is the
    /// only place a streaming backend can report it — the total is not known until the stream ends. A backend
    /// that reports none records none, exactly as on the inline path.</para></remarks>
    public async IAsyncEnumerable<GenerationChunk> StreamAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (await OverBudgetAsync(request.Consumer, ct).ConfigureAwait(false) is { } reason)
        {
            yield return GenerationChunk.Failure(GenerationVerdict.Refused, reason);
            yield break;
        }

        await foreach (var chunk in inner.StreamAsync(candidates, request, ct).ConfigureAwait(false))
        {
            if (chunk.Usage is not null)
                await RecordAsync(request.Consumer, chunk.Usage, ct).ConfigureAwait(false);
            yield return chunk;
        }
    }

    /// <summary>Record a reported cost into the shared ledger as a zero-token, cost-only entry — one place
    /// that knows how generation spend maps onto the ledger.
    /// <para><b>Internal.</b> Its previous doc said "public so the durable-render handler can record …", and
    /// that handler is <c>GenerationRenderJobHandler</c>, in THIS assembly — so the stated reason was
    /// satisfied by <c>internal</c> and the surface was a permanent promise nothing outside had asked for.
    /// Recording generation spend is something the library's own components do on a caller's behalf; if it
    /// ever becomes a consumer capability it belongs on <see cref="IUsageTracker"/>, not on a router decorator.</para></summary>
    internal static ValueTask RecordAsync(
        IUsageTracker tracker, string consumer, GenerationUsage? usage, CancellationToken ct = default) =>
        usage?.CostUsd is { } cost && cost > 0
            ? tracker.RecordAsync(consumer, new LlmUsage(0, 0, 0, cost), ct)
            : ValueTask.CompletedTask;

    private ValueTask RecordAsync(string consumer, GenerationUsage? usage, CancellationToken ct) =>
        RecordAsync(tracker, consumer, usage, ct);

    /// <summary>The refusal reason when a COST cap that applies to <paramref name="consumer"/> has been
    /// reached — the global cap (vs the global total) or the consumer's own (vs its total) — else null.</summary>
    private async ValueTask<string?> OverBudgetAsync(string consumer, CancellationToken ct)
    {
        var budget = options.Budget;

        if (budget.MaxCostUsd is { } cap)
        {
            var global = await tracker.TotalAsync(ct: ct).ConfigureAwait(false);
            if (global.CostUsd >= cap) return Refuse("global cost budget", cap);
        }

        if (budget.PerConsumer.TryGetValue(consumer, out var mine) && mine.MaxCostUsd is { } consumerCap)
        {
            var spent = await tracker.TotalAsync(consumer, ct).ConfigureAwait(false);
            if (spent.CostUsd >= consumerCap) return Refuse($"consumer '{consumer}' cost budget", consumerCap);
        }

        return null;
    }

    private string Refuse(string label, double cap)
    {
        var reason = $"{label} of {cap.ToString(CultureInfo.InvariantCulture)} reached";
        _logger.LogInformation("generation usage budget refusal: {Reason}", reason);
        LyntaiDiagnostics.RecordBudgetRefusal(label);
        return reason;
    }
}
