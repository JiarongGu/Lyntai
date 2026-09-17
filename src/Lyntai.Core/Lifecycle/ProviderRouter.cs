using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Lifecycle;

/// <summary>Fallback routing for ONE call shape, over whichever registered backends implement it.
///
/// <para><b>What it is for: a kind this library has never heard of.</b> An application closing
/// <see cref="IProviderCall{TRequest,TResponse}"/> over its own types gets candidate selection, dead-host
/// cooldown, admission and fallback from here, without Core knowing what the kind is
/// (<c>docs/DECISIONS.md</c> <b>D153</b>). Core uses it for the kinds that had no routing of their own.</para>
///
/// <para><b>It does NOT replace <c>LlmRouter</c> or <c>GenerationRouter</c>, deliberately.</b> Those two
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
/// <param name="logger">Null = no logging.</param>
public sealed class ProviderRouter<TRequest, TResponse>(
    IEnumerable<IModelProvider> providers,
    Func<ProviderVerdict, string, TResponse> synthesize,
    Func<ProviderCapabilities, bool>? serves = null,
    RoutingPolicy? policy = null,
    DeadHostTracker? deadHosts = null,
    IProviderAdmission? admission = null,
    Func<IModelProvider, ProviderKey?>? configuration = null,
    ILogger? logger = null)
    where TResponse : IProviderOutcome
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;
    private readonly RoutingPolicy _policy = policy ?? new RoutingPolicy();
    private readonly Func<IModelProvider, ProviderKey?> _configuration = configuration ?? (_ => null);

    /// <summary>The registered backends that serve this call shape AND report themselves usable, in
    /// registration order.
    ///
    /// <para>Availability is read per call rather than cached: a backend can become usable between one call
    /// and the next, and a cached "unavailable" would outlive the outage that caused it.</para></summary>
    public IReadOnlyList<IProviderCall<TRequest, TResponse>> Capable() =>
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
        TResponse? last = default;          // the last SUBSTANTIVE failure — what the caller is told
        TResponse? lastBlameless = default; // …kept apart, so it answers only when nothing really failed
        var tried = 0;

        var capable = Capable();
        var sole = capable.Count == 1;
        foreach (var provider in capable)
        {
            var key = CooldownKey(provider);
            if (!(sole && _policy.ExemptSoleCandidate) && deadHosts?.IsDead(key) == true)
            {
                _logger.LogDebug("router: skipping {Provider} — dead-host cooldown", provider.Id);
                continue;
            }

            tried++;
            var retries = 0;
            while (true)
            {
                var response = await AttemptAsync(provider, request, ct).ConfigureAwait(false);
                if (response.Verdict == ProviderVerdict.Ok)
                {
                    deadHosts?.RecordSuccess(key);
                    return response;
                }

                var action = _policy.ActionFor(response.Verdict);
                if (action == FallbackAction.Surface) return response;

                if (response.Verdict.IsBlameless()) lastBlameless = response; else last = response;

                if (action == FallbackAction.CooldownAndAdvance)
                {
                    deadHosts?.MarkDead(key);
                    break;
                }
                if (action == FallbackAction.PenalizeAndAdvance)
                {
                    if (_policy.ShouldRetrySameCandidate(response.Verdict, ++retries))
                    {
                        if (_policy.RetryBackoff > TimeSpan.Zero)
                            await Task.Delay(_policy.RetryBackoff, ct).ConfigureAwait(false);
                        continue; // retries are part of ONE attempt — no failure recorded yet
                    }
                    // exactly ONE failure per request, never one per retry: recording per retry would cross
                    // the dead-host threshold inside a single call
                    deadHosts?.RecordFailure(key);
                }
                break;
            }
        }

        if (last is not null) return last;
        if (lastBlameless is not null) return lastBlameless;
        return synthesize(
            tried == 0 ? ProviderVerdict.NotConfigured : ProviderVerdict.Failed,
            tried == 0
                ? "no registered backend serves this call (none capable, or every one is on cooldown)"
                : $"every capable backend failed ({tried} tried)");
    }

    /// <summary>One attempt at one backend: take an admission permit, call it, and turn a throw into a
    /// classified verdict so a backend that throws gets the same policy as one that answers.
    ///
    /// <para>The permit is scoped with <c>using</c>, so the return, a rethrown caller cancel and a classified
    /// throw all release it — a permit that escapes pins its gate for the life of the process.</para></summary>
    private async Task<TResponse> AttemptAsync(
        IProviderCall<TRequest, TResponse> provider, TRequest request, CancellationToken ct)
    {
        using var permit = admission is not null && _configuration(provider) is { } key
            ? await admission.EnterAsync(key, ct).ConfigureAwait(false)
            : null;
        try
        {
            return await provider.CallAsync(request, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller-initiated cancel is not a backend failure
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "router: {Provider} threw; classifying and advancing", provider.Id);
            return synthesize(ProviderVerdictClassifier.FromThrown(ex), $"{provider.Id}: {ex.Message}");
        }
    }

    private string CooldownKey(IModelProvider provider) =>
        _configuration(provider)?.ToString() ?? provider.Id;
}
