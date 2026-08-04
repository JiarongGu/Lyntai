namespace Lyntai.Generation.Routing;

/// <summary>The default <see cref="IGenerationRouter"/>: capability pre-filter, then verdict-driven fallback.
///
/// Fallback semantics come from <see cref="GenerationRoutingPolicy"/>, whose defaults mirror the LLM
/// router's (design §6) so one mental model covers both:
/// <list type="bullet">
/// <item><see cref="GenerationVerdict.Refused"/> SURFACES — a content refusal is not a transport fault, and
///   quietly re-submitting a refused prompt to another vendor is not a library's decision. A host that
///   deliberately pairs a hosted backend with a permissive local one can override exactly that.</item>
/// <item><see cref="GenerationVerdict.NotConfigured"/> and <see cref="GenerationVerdict.Unsupported"/> ADVANCE without
///   blame — the backend isn't broken, it just isn't the one for this request.</item>
/// <item>everything else advances, keeping the FIRST substantive failure as the reported reason: the first
///   backend's error explains the run better than the last one's.</item>
/// </list>
/// Cooldown/penalty bookkeeping is deliberately NOT here yet — see the plan's Plan 5, which composes
/// governance (budget, rate limits, dead-host cooldown) rather than growing a second copy of it.</summary>
/// <param name="providers">The registered backends.</param>
/// <param name="policy">Per-verdict fallback behaviour; null = the defaults above.</param>
public sealed class GenerationRouter(
    IEnumerable<IGenerationProvider> providers,
    GenerationRoutingPolicy? policy = null) : IGenerationRouter
{
    private readonly IReadOnlyList<IGenerationProvider> _providers = [.. providers];
    private readonly GenerationRoutingPolicy _policy = policy ?? new GenerationRoutingPolicy();

    /// <inheritdoc/>
    public async Task<GenerationResult> GenerateAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        GenerationResult? firstFailure = null;
        var tried = 0;

        foreach (var (provider, resolved) in Capable(candidates, request, GenerationDelivery.Inline))
        {
            tried++;
            var result = await provider.GenerateAsync(resolved, ct).ConfigureAwait(false);
            if (result.IsOk) return result;

            // not-configured / unsupported aren't faults worth reporting over a real failure — but every
            // other verdict is remembered BEFORE the surface check, so advancing past one (a host that
            // configured Refused -> Advance) still reports it when nothing else succeeds
            if (result.Verdict is not (GenerationVerdict.NotConfigured or GenerationVerdict.Unsupported))
                firstFailure ??= result;

            if (_policy.ActionFor(result.Verdict) == GenerationFallbackAction.Surface) return result;
        }

        if (tried == 0)
            return GenerationResult.Failure(GenerationVerdict.Unsupported,
                $"no capable media backend for kind '{request.Kind}' via {GenerationDelivery.Inline} " +
                $"among [{string.Join(", ", candidates.Select(c => c.ProviderId))}]");

        return firstFailure ?? GenerationResult.Failure(GenerationVerdict.NotConfigured,
            "every capable backend reported it is not configured");
    }

    /// <inheritdoc/>
    public async Task<GenerationSubmission> SubmitAsync(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, CancellationToken ct = default)
    {
        foreach (var (provider, resolved) in Capable(candidates, request, GenerationDelivery.Job))
        {
            if (provider is not IGenerationJobProvider job) continue;   // capability says Job; the type must agree
            var operation = await job.SubmitAsync(resolved, ct).ConfigureAwait(false);
            if (operation.Status != GenerationOperationStatus.Failed)
                return new GenerationSubmission(provider.Id, operation);
        }

        return new GenerationSubmission("", new GenerationOperation("", GenerationOperationStatus.Failed,
            Detail: $"no capable media backend accepted a '{request.Kind}' job among " +
                $"[{string.Join(", ", candidates.Select(c => c.ProviderId))}]"));
    }

    /// <summary>Candidates that exist, are registered, and DECLARE they can serve this request/delivery —
    /// with the candidate's model applied to the request when it pins one.</summary>
    private IEnumerable<(IGenerationProvider Provider, GenerationRequest Request)> Capable(
        IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request, GenerationDelivery delivery)
    {
        foreach (var candidate in candidates)
        {
            var provider = _providers.FirstOrDefault(p =>
                string.Equals(p.Id, candidate.ProviderId, StringComparison.OrdinalIgnoreCase));
            if (provider is null) continue;   // an unknown id is a config typo, not a crash

            var resolved = candidate.Model is { Length: > 0 } model ? request with { Model = model } : request;
            if (!provider.Capabilities.Supports(resolved, delivery)) continue;

            yield return (provider, resolved);
        }
    }
}
