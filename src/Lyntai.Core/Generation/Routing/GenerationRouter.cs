using System.Diagnostics;
using Lyntai.Diagnostics;
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
public sealed class GenerationRouter(
    IEnumerable<IGenerationProvider> providers,
    GenerationRoutingPolicy? policy = null,
    DeadHostTracker? deadHosts = null) : IGenerationRouter
{
    private readonly IReadOnlyList<IGenerationProvider> _providers = [.. providers];
    private readonly GenerationRoutingPolicy _policy = policy ?? new GenerationRoutingPolicy();

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
            if (IsBenched(provider.Id, capable.Count)) { benched++; continue; }

            tried++;
            var result = await AttemptAsync(provider, resolved, ct).ConfigureAwait(false);
            if (result.IsOk)
            {
                deadHosts?.RecordSuccess(CooldownKey(provider.Id));
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
                    deadHosts?.MarkDead(CooldownKey(provider.Id));
                    break;
                case GenerationFallbackAction.PenalizeAndAdvance:
                    deadHosts?.RecordFailure(CooldownKey(provider.Id));
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
            if (IsBenched(provider.Id, capable.Count)) { benched++; continue; }

            var started = Stopwatch.GetTimestamp();
            using var span = LyntaiDiagnostics.StartGeneration("submit", provider.Id, resolved.Kind, resolved.Model);
            var operation = await job.SubmitAsync(resolved, ct).ConfigureAwait(false);
            LyntaiDiagnostics.RecordSubmission(span, provider.Id, resolved.Kind, operation.Id, operation.Status,
                Stopwatch.GetElapsedTime(started).TotalSeconds);

            if (operation.Status != GenerationOperationStatus.Failed)
            {
                deadHosts?.RecordSuccess(CooldownKey(provider.Id));
                return new GenerationSubmission(provider.Id, operation);
            }

            // a queue that won't take the job is exactly the dead-host case the LLM side benches for: the
            // next submission a second later has no reason to fare better
            deadHosts?.RecordFailure(CooldownKey(provider.Id));
        }

        return new GenerationSubmission("", new GenerationOperation("", GenerationOperationStatus.Failed,
            Detail: benched > 0
                ? $"every capable media backend for a '{request.Kind}' job is on dead-host cooldown"
                : $"no capable media backend accepted a '{request.Kind}' job among " +
                  $"[{string.Join(", ", candidates.Select(c => c.ProviderId))}]"));
    }

    /// <summary>One backend attempt, wrapped in a span + duration/cost metrics. Per ATTEMPT, not per request:
    /// a trace of a fallback run has to show the attempt that failed as well as the one that worked.</summary>
    private static async Task<GenerationResult> AttemptAsync(
        IGenerationProvider provider, GenerationRequest request, CancellationToken ct)
    {
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

    /// <summary>Whether this backend is benched — honouring the sole-candidate exemption, so the only capable
    /// backend is always tried.</summary>
    private bool IsBenched(string providerId, int capableCount) =>
        deadHosts is not null &&
        !(capableCount == 1 && _policy.ExemptSoleCandidate) &&
        deadHosts.IsDead(CooldownKey(providerId));

    /// <summary>The tracker is shared with the LLM router, so keys carry their domain: a host with a chat
    /// provider and an image backend both called "openai" must not have one bench the other.</summary>
    private static string CooldownKey(string providerId) => $"generation::{providerId}";

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
