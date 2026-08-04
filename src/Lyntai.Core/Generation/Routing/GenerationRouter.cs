using System.Diagnostics;
using Lyntai.Diagnostics;
using Lyntai.Lifecycle;
using Lyntai.Llm.Routing;

namespace Lyntai.Generation.Routing;

/// <summary>The default <see cref="IGenerationRouter"/>: capability pre-filter, then verdict-driven fallback
/// with dead-host cooldown, plus a span and metrics per attempt.
///
/// Fallback semantics come from <see cref="GenerationRoutingPolicy"/>, whose defaults mirror the LLM
/// router's (design §6) so one mental model covers both:
/// <list type="bullet">
/// <item><see cref="GenerationVerdict.Refused"/> SURFACES — a content refusal is not a transport fault, and
///   quietly re-submitting a refused prompt to another vendor is not a library's decision. A host that
///   deliberately pairs a hosted backend with a permissive local one can override exactly that.</item>
/// <item><see cref="GenerationVerdict.NotConfigured"/> and <see cref="GenerationVerdict.Unsupported"/> ADVANCE without
///   blame — the backend isn't broken, it just isn't the one for this request.</item>
/// <item><see cref="GenerationVerdict.RateLimited"/> / <see cref="GenerationVerdict.AuthFailed"/> BENCH the backend for
///   the cooldown window; a timeout or an outright failure counts toward the threshold.</item>
/// <item>everything else advances, keeping the FIRST substantive failure as the reported reason: the first
///   backend's error explains the run better than the last one's.</item>
/// </list></summary>
/// <param name="providers">The registered backends.</param>
/// <param name="policy">Per-verdict fallback behaviour; null = the defaults above.</param>
/// <param name="deadHosts">Cooldown bookkeeping — the SAME <see cref="DeadHostTracker"/> the LLM router uses,
/// deliberately: "this host keeps failing, stop asking" is transport bookkeeping keyed by a string, not an LLM
/// concept, and a second copy would be a second set of bugs (and a second threshold to configure). Keys are
/// prefixed per domain so a chat outage never benches a generation backend that happens to share its id.
/// Null disables cooldown entirely (a hand-built router in a test).</param>
/// <param name="configuration">Which CONFIGURATION a provider is running under, used to key dead-host
/// cooldown and admission. Null (or a null return) = key cooldown on
/// <see cref="Lyntai.Lifecycle.IProviderIdentity.Id"/> and apply no admission, which is the historical
/// behaviour and correct for a single-configuration deployment. Supply one when several configurations of a
/// backend id are live at once — otherwise one tenant's rate limit benches every other tenant sharing that
/// backend, and two consumers of one downed self-hosted host fail to share a bench that would have spared
/// them both. <see cref="IProviderPool{TProvider}.TryGetKey"/> is the intended source.
///
/// <para><b>Must return a STABLE key for a given instance.</b> One routing attempt invokes it more than once
/// — for the bench check, for admission, and for the record that follows — so a delegate whose answer varies
/// between those calls would record cooldown under a key different from the one checked, producing a bench
/// that silently never takes effect. Look the key up (as a pool does); never recompute it from live
/// state.</para></param>
/// <param name="admission">Bounds concurrent attempts per configuration — for a locally-run engine where
/// simultaneous renders contend for one CPU or GPU. Null = unbounded. Applied HERE rather than by
/// wrapping a provider, because a wrapper implementing only <see cref="IGenerationProvider"/> erases the
/// optional capability interfaces (<see cref="IGenerationJobProvider"/>) this router type-tests, which
/// would silently stop every queued render from routing.</param>
public sealed class GenerationRouter(
    IEnumerable<IGenerationProvider> providers,
    GenerationRoutingPolicy? policy = null,
    DeadHostTracker? deadHosts = null,
    Func<IGenerationProvider, ProviderKey?>? configuration = null,
    ProviderAdmission? admission = null) : IGenerationRouter
{
    private readonly IReadOnlyList<IGenerationProvider> _providers = [.. providers];
    private readonly GenerationRoutingPolicy _policy = policy ?? new GenerationRoutingPolicy();

    // resolved once: the no-delegate case must cost nothing per attempt, and a null-returning delegate must
    // be indistinguishable from no delegate at all
    private readonly Func<IGenerationProvider, ProviderKey?> _configuration = configuration ?? (_ => null);

    /// <inheritdoc/>
    public async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, GenerationDelivery.Inline);
        GenerationResult? firstFailure = null;
        var tried = 0;
        var benched = 0;

        foreach (var (provider, resolved) in capable)
        {
            if (IsBenched(provider, capable.Count)) { benched++; continue; }

            tried++;
            var result = await AttemptAsync(provider, resolved, ct).ConfigureAwait(false);
            if (result.IsOk)
            {
                deadHosts?.RecordSuccess(CooldownKey(provider));
                return result;
            }

            // not-configured / unsupported aren't faults worth reporting over a real failure — but every
            // other verdict is remembered BEFORE the surface check, so advancing past one (a host that
            // configured Refused -> Advance) still reports it when nothing else succeeds
            if (result.Verdict is not (GenerationVerdict.NotConfigured or GenerationVerdict.Unsupported))
                firstFailure ??= result;

            switch (_policy.ActionFor(result.Verdict))
            {
                case GenerationFallbackAction.Surface:
                    return result;
                case GenerationFallbackAction.CooldownAndAdvance:
                    deadHosts?.MarkDead(CooldownKey(provider));
                    break;
                case GenerationFallbackAction.PenalizeAndAdvance:
                    deadHosts?.RecordFailure(CooldownKey(provider));
                    break;
            }
        }

        if (tried == 0)
            return GenerationResult.Failure(
                benched > 0 ? GenerationVerdict.RateLimited : GenerationVerdict.Unsupported,
                benched > 0
                    ? $"every capable media backend for kind '{request.Kind}' is on dead-host cooldown " +
                      $"({benched} of [{string.Join(", ", candidates.Select(c => c.ProviderId))}])"
                    : $"no capable media backend for kind '{request.Kind}' via {GenerationDelivery.Inline} " +
                      $"among [{string.Join(", ", candidates.Select(c => c.ProviderId))}]");

        return firstFailure ?? GenerationResult.Failure(GenerationVerdict.NotConfigured,
            "every capable backend reported it is not configured");
    }

    /// <inheritdoc/>
    public async Task<GenerationSubmission> SubmitAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        var capable = Capable(candidates, request, GenerationDelivery.Job);
        var benched = 0;

        foreach (var (provider, resolved) in capable)
        {
            if (provider is not IGenerationJobProvider job) continue;   // capability says Job; the type must agree
            if (IsBenched(provider, capable.Count)) { benched++; continue; }

            // submitting is what commits the money, so it respects the same bound as an inline render. The
            // permit is scoped to THIS iteration: a submission that fails releases it before the next
            // candidate is tried.
            using (await EnterAdmissionAsync(provider, ct).ConfigureAwait(false))
            {
                var started = Stopwatch.GetTimestamp();
                using var span = LyntaiDiagnostics.StartGeneration("submit", provider.Id, resolved.Kind, resolved.Model);
                var operation = await job.SubmitAsync(resolved, ct).ConfigureAwait(false);
                LyntaiDiagnostics.RecordSubmission(span, provider.Id, resolved.Kind, operation.Id, operation.Status,
                    Stopwatch.GetElapsedTime(started).TotalSeconds);

                if (operation.Status != GenerationOperationStatus.Failed)
                {
                    deadHosts?.RecordSuccess(CooldownKey(provider));
                    return new GenerationSubmission(provider.Id, operation);
                }

                // a queue that won't take the job is exactly the dead-host case the LLM side benches for: the
                // next submission a second later has no reason to fare better
                deadHosts?.RecordFailure(CooldownKey(provider));
            }
        }

        return new GenerationSubmission("", new GenerationOperation("", GenerationOperationStatus.Failed,
            Detail: benched > 0
                ? $"every capable media backend for a '{request.Kind}' job is on dead-host cooldown"
                : $"no capable media backend accepted a '{request.Kind}' job among " +
                  $"[{string.Join(", ", candidates.Select(c => c.ProviderId))}]"));
    }

    /// <summary>One backend attempt, wrapped in a span + duration/cost metrics. Per ATTEMPT, not per request:
    /// a trace of a fallback run has to show the attempt that failed as well as the one that worked.</summary>
    private async Task<GenerationResult> AttemptAsync(
        IGenerationProvider provider, GenerationRequest request, CancellationToken ct)
    {
        // the permit is taken BEFORE the clock starts, so a queued render's wait never inflates the
        // backend's reported latency
        using var permit = await EnterAdmissionAsync(provider, ct).ConfigureAwait(false);

        var started = Stopwatch.GetTimestamp();
        using var span = LyntaiDiagnostics.StartGeneration("generate", provider.Id, request.Kind, request.Model);
        var result = await provider.GenerateAsync(request, ct).ConfigureAwait(false);
        LyntaiDiagnostics.RecordGeneration(span, provider.Id, request.Kind, result.Verdict,
            // an Ok result always has artifacts (GenerationResult.Success enforces it), so the count is
            // reported even by a backend that returns no usage of its own
            result.IsOk ? result.Usage ?? new GenerationUsage(Count: result.Artifacts.Count) : result.Usage,
            Stopwatch.GetElapsedTime(started).TotalSeconds, result.Detail);
        return result;
    }

    /// <summary>Take a concurrency permit for this provider's CONFIGURATION, or nothing at all when no
    /// admission is wired or the configuration is unknown. Never returns a handle the caller may skip
    /// disposing: a permit that is not returned pins its gate for the life of the process, so every call site
    /// scopes the result with <c>using</c> and lets success, failure, a throw and cancellation all release
    /// it the same way.</summary>
    private async ValueTask<IDisposable?> EnterAdmissionAsync(IGenerationProvider provider, CancellationToken ct) =>
        admission is not null && _configuration(provider) is { } key
            ? await admission.EnterAsync(key, ct).ConfigureAwait(false)
            : null;

    /// <summary>Whether this backend is benched — honouring the sole-candidate exemption, so the only capable
    /// backend is always tried.</summary>
    private bool IsBenched(IGenerationProvider provider, int capableCount) =>
        deadHosts is not null &&
        !(capableCount == 1 && _policy.ExemptSoleCandidate) &&
        deadHosts.IsDead(CooldownKey(provider));

    /// <summary>The tracker is shared with the LLM router, so keys carry their domain: a host with a chat
    /// provider and an image backend both called "openai" must not have one bench the other.
    ///
    /// <para>Within the domain the key is the CONFIGURATION when one is known, falling back to the backend id
    /// otherwise — the two configurations of one backend id that a pool keeps live must bench independently,
    /// while two consumers of one downed self-hosted host must share a bench.</para></summary>
    private string CooldownKey(IGenerationProvider provider) =>
        $"generation::{_configuration(provider)?.ToString() ?? provider.Id}";

    /// <summary>Candidates that exist, are registered, and DECLARE they can serve this request/delivery —
    /// with the candidate's model applied to the request when it pins one. Materialized because the
    /// sole-candidate cooldown exemption needs to know how many there are before trying the first.</summary>
    private List<(IGenerationProvider Provider, GenerationRequest Request)> Capable(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, GenerationDelivery delivery)
    {
        var capable = new List<(IGenerationProvider, GenerationRequest)>();
        foreach (var candidate in candidates)
        {
            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Id, candidate.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (provider is null) continue;   // an unknown id is a config typo, not a crash

            var resolved = candidate.Model is { Length: > 0 } model ? request with { Model = model } : request;
            if (!provider.Capabilities.Supports(resolved, delivery)) continue;

            capable.Add((provider, resolved));
        }
        return capable;
    }
}
