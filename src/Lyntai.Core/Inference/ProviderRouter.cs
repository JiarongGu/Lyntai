using System.Diagnostics;
using Lyntai.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Inference;

/// <summary>Fallback routing for ONE call shape, over whichever registered backends implement it.
///
/// <para><b>What it is for: a kind this library has never heard of.</b> An application closing
/// <see cref="IProviderCall{TRequest,TResponse}"/> over its own types gets candidate selection, dead-host
/// cooldown, admission and fallback from here, without Core knowing what the kind is
/// (<c>docs/DECISIONS.md</c> <b>D153</b>). Core uses it for the kinds that had no routing of their own.</para>
///
/// <para><b>It does NOT replace <c>TextRouter</c> or <c>MediaRouter</c>, deliberately.</b> Those two
/// differ in eight ways that are each recorded and load-bearing — last-versus-first failure, retries present
/// versus absent, one synthetic failure versus two, and so on — and folding them in would mean eight
/// injection points on the most load-bearing code here. Converging them is its own decision, not a
/// consequence of this one.</para></summary>
/// <param name="providers">Every registered backend. Those not implementing the call shape are skipped.</param>
/// <param name="synthesize">Builds the reply for "nothing answered" — the one thing a generic router cannot
/// construct, because only the kind knows what an empty response looks like.</param>
/// <param name="policy">What to do per verdict. Defaults to <see cref="RoutingPolicy"/>'s own defaults.</param>
/// <param name="deadHosts">Cooldown bookkeeping. Null = no cooldown, which is what this kind had before.</param>
/// <param name="admission">Bounds concurrent calls per configuration. Null = unbounded.</param>
/// <param name="configuration">Which CONFIGURATION a backend runs under, for cooldown and admission keys.
/// Must return a STABLE key per instance — see <c>llm-and-router.md</c>; a varying answer records a bench
/// under a key nobody checks.</param>
/// <param name="serves">Whether a backend's DECLARED capabilities cover this call, asked in addition to the
/// type test. <b>Both are required and neither is redundant</b>: one backend class can implement a call
/// shape and be CONFIGURED not to serve it — <c>HttpModelProvider</c> implements the vector shape whatever
/// its <c>Produces</c> says — so the type test alone would route a chat-only endpoint an embed call. Null
/// asks the type test only, which is right for a shape whose implementers always serve it.</param>
/// <param name="governance">The one wallet, applied to any request the router can ATTRIBUTE
/// (<see cref="IConsumerTagged"/>): budget caps checked before a backend spends, a client-side rate
/// limiter, and the response's reported <see cref="IProviderOutcome.Usage"/> recorded under the request's
/// consumer. Null — the hand-composed default — governs nothing; the factory supplies one from whatever
/// the container holds (<c>docs/DECISIONS.md</c> <b>D163</b>).</param>
/// <param name="cooldownScope">Prefix namespacing this router's cooldown keys per DOMAIN (the rule
/// <c>MediaRouter</c>'s <c>generation::</c> prefix already applies), so a reranker's outage never benches
/// an embedder that happens to share an id — reachable in a default configuration, because
/// <c>AddOnnxProvider</c> defaults both ids to <c>"onnx"</c>. Null keys on the bare configuration/id, which
/// is only safe where one kind is in play; <see cref="IProviderRouterFactory"/> always scopes.</param>
/// <param name="logger">Null = no logging.</param>
public sealed class ProviderRouter<TRequest, TResponse>(
    IEnumerable<IModelProvider> providers,
    Func<ProviderVerdict, string, TResponse> synthesize,
    Func<ProviderCapabilities, bool>? serves = null,
    RoutingPolicy? policy = null,
    DeadHostTracker? deadHosts = null,
    IProviderAdmission? admission = null,
    Func<IModelProvider, ProviderKey?>? configuration = null,
    RouterGovernance? governance = null,
    string? cooldownScope = null,
    ILogger? logger = null)
    where TResponse : class, IProviderOutcome
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly RoutingPolicy _policy = policy ?? new RoutingPolicy();
    private readonly RouterBookkeeping _bookkeeping = new(deadHosts, admission, configuration, cooldownScope);

    /// <summary>The registered backends that serve this call shape AND report themselves usable, in
    /// registration order. Availability is read per call rather than cached: a cached "unavailable" would
    /// outlive the outage that caused it.</summary>
    private List<IProviderCall<TRequest, TResponse>> Capable() =>
        [.. providers.OfType<IProviderCall<TRequest, TResponse>>().Where(Serves)];

    /// <summary>The two questions a candidate must answer yes to, asked in one place so the list and the
    /// short-circuit below cannot drift: does it IMPLEMENT this call, and does it DECLARE it.</summary>
    private bool Serves(IProviderCall<TRequest, TResponse> provider) =>
        provider.IsAvailable && (serves is null || serves(provider.Capabilities));

    /// <summary>Whether anything can serve this shape at all.
    ///
    /// <para><b>Short-circuits and allocates nothing</b>, because callers sit on hot paths and
    /// <see cref="IModelProvider.IsAvailable"/> is not always free — a CLI backend's resolves a command on
    /// PATH. Ask the cheap question and stop at the first backend that answers.</para></summary>
    public bool CanServe() => providers.OfType<IProviderCall<TRequest, TResponse>>().Any(Serves);

    /// <summary>Try each capable backend in turn until one answers Ok, applying the policy's action to every
    /// non-Ok verdict on the way.
    ///
    /// <para><b>The failure a caller is told about is the LAST substantive one</b>, kept apart from blameless
    /// verdicts so "nothing is configured" can never mask a backend that is genuinely down
    /// (<see cref="ProviderVerdictExtensions.IsBlameless"/>). Only a run where nothing answered at all
    /// reaches <c>synthesize</c>.</para>
    ///
    /// <para>Cancellation belongs to the caller and propagates; a backend's own failure — thrown or
    /// returned — is classified through <see cref="ProviderVerdictClassifier"/> and routed on.</para></summary>
    public async Task<TResponse> CallAsync(TRequest request, CancellationToken ct = default)
    {
        // Governance first, and only for a request that can be ATTRIBUTED: a budget refusal must cost no
        // backend call, and a client-side rate refusal is nobody's fault — both return before the loop, so
        // no host is tried, penalized or benched for them (D163).
        string? consumer = null;
        if (governance is { } gov && request is IConsumerTagged tagged)
        {
            consumer = tagged.Consumer ?? ProviderConsumers.Default;
            if (gov.Tracker is { } gate && await Budgeting.BudgetGate.OverBudgetAsync(
                    gov.Options.Budget, gate, consumer, includeTokens: true, _logger, ct).ConfigureAwait(false)
                is { } reason)
                return synthesize(ProviderVerdict.Refused, reason);
            if (gov.Limiter is { } limiter && await RateLimiting.RateGate.RefuseAsync(
                    limiter, consumer, RateLimiting.RateGate.Exceeded, _logger, ct).ConfigureAwait(false)
                is { } throttled)
                return synthesize(ProviderVerdict.RateLimited, throttled);
        }

        var response = await RouteAsync(request, ct).ConfigureAwait(false);
        if (consumer is not null && governance?.Tracker is { } tracker && response.Usage is { } usage)
            await tracker.RecordAsync(consumer, usage, ct).ConfigureAwait(false);
        return response;
    }

    private async Task<TResponse> RouteAsync(TRequest request, CancellationToken ct)
    {
        TResponse? last = default;          // the last SUBSTANTIVE failure — what the caller is told
        TResponse? lastBlameless = default; // …kept apart, so it answers only when nothing really failed
        var benched = 0;

        var capable = Capable();
        var sole = capable.Count == 1;
        foreach (var provider in capable)
        {
            var key = _bookkeeping.Key(provider);
            if (_bookkeeping.IsBenched(key, sole, _policy.ExemptSoleCandidate))
            {
                _logger.LogDebug("router: skipping {Provider} — dead-host cooldown", provider.Id);
                benched++;
                continue;
            }

            var returned = await CandidateAttempt.RunAsync(
                () => AttemptAsync(provider, request, ct),
                response => { if (response.Verdict.IsBlameless()) lastBlameless = response; else last = response; },
                _policy, deadHosts, key, onRetry: null, ct).ConfigureAwait(false);
            if (returned is not null) return returned;
        }

        // every attempted backend filled a slot or returned, so reaching the synthetic reply means none was tried:
        // benched is a fault (the backend they configured is down), only "nothing capable" is blameless
        if ((last ?? lastBlameless) is { } answer) return answer;
        return benched > 0
            ? synthesize(ProviderVerdict.Failed, $"every capable backend is on dead-host cooldown ({benched} benched)")
            : synthesize(ProviderVerdict.NotConfigured, "no registered backend serves this call");
    }

    /// <summary>One attempt at one backend: take an admission permit, call it, and turn a throw into a
    /// classified verdict so a backend that throws gets the same policy as one that answers.
    ///
    /// <para>The permit is scoped with <c>using</c>, so the return, a rethrown caller cancel and a classified
    /// throw all release it — a permit that escapes pins its gate for the life of the process.</para></summary>
    private async Task<TResponse> AttemptAsync(
        IProviderCall<TRequest, TResponse> provider, TRequest request, CancellationToken ct)
    {
        // the permit is taken BEFORE the span opens, so a queued call's wait never inflates the reported latency
        using var permit = await _bookkeeping.EnterAsync(provider, ct).ConfigureAwait(false);
        using var span = LyntaiDiagnostics.StartCall(Operation, provider.Id);
        var started = Stopwatch.GetTimestamp();
        TResponse response;
        try
        {
            response = await provider.CallAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller-initiated cancel is not a backend failure
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "router: {Provider} threw; classifying and advancing", provider.Id);
            response = synthesize(ProviderVerdictClassifier.FromThrown(ex), $"{provider.Id}: {ex.Message}");
        }
        LyntaiDiagnostics.RecordCallOutcome(span, Operation, provider.Id, model: null, response.Verdict,
            response.Usage, cacheReadTokens: 0, Stopwatch.GetElapsedTime(started).TotalSeconds, response.Detail);
        return response;
    }

    /// <summary>The span's <c>gen_ai.operation.name</c>: the GenAI convention's <c>embeddings</c> for the vector
    /// shape, <c>rerank</c> for the score shape, and an application kind's own name otherwise.</summary>
    private static readonly string Operation = ProviderRouterFactory.KindOf(typeof(TRequest)) switch
    {
        "vector" => "embeddings",
        "score" => "rerank",
        var kind => kind,
    };
}
