namespace Lyntai.Media.Routing;

/// <summary>The default <see cref="IMediaRouter"/>: capability pre-filter, then verdict-driven fallback.
///
/// Fallback semantics deliberately mirror the LLM router's (design §6) so one mental model covers both:
/// <list type="bullet">
/// <item><see cref="MediaVerdict.Refused"/> SURFACES — a content refusal is not a transport fault, and
///   quietly re-submitting a refused prompt to another vendor is not a library's decision.</item>
/// <item><see cref="MediaVerdict.NotConfigured"/> and <see cref="MediaVerdict.Unsupported"/> ADVANCE without
///   blame — the backend isn't broken, it just isn't the one for this request.</item>
/// <item>everything else advances, keeping the FIRST substantive failure as the reported reason: the first
///   backend's error explains the run better than the last one's.</item>
/// </list>
/// Cooldown/penalty bookkeeping is deliberately NOT here yet — see the plan's Plan 5, which composes
/// governance (budget, rate limits, dead-host cooldown) rather than growing a second copy of it.</summary>
public sealed class MediaRouter(IEnumerable<IMediaProvider> providers) : IMediaRouter
{
    private readonly IReadOnlyList<IMediaProvider> _providers = [.. providers];

    /// <inheritdoc/>
    public async Task<MediaResult> GenerateAsync(
        IReadOnlyList<MediaCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        MediaResult? firstFailure = null;
        var tried = 0;

        foreach (var (provider, resolved) in Capable(candidates, request, MediaDelivery.Inline))
        {
            tried++;
            var result = await provider.GenerateAsync(resolved, ct).ConfigureAwait(false);
            if (result.IsOk) return result;

            // a refusal is the backend's judgement on the CONTENT — surface it rather than shopping around
            if (result.Verdict == MediaVerdict.Refused) return result;

            // not-configured / unsupported aren't faults worth reporting over a real failure
            if (result.Verdict is not (MediaVerdict.NotConfigured or MediaVerdict.Unsupported))
                firstFailure ??= result;
        }

        if (tried == 0)
            return MediaResult.Failure(MediaVerdict.Unsupported,
                $"no capable media backend for kind '{request.Kind}' via {MediaDelivery.Inline} " +
                $"among [{string.Join(", ", candidates.Select(c => c.ProviderId))}]");

        return firstFailure ?? MediaResult.Failure(MediaVerdict.NotConfigured,
            "every capable backend reported it is not configured");
    }

    /// <inheritdoc/>
    public async Task<MediaSubmission> SubmitAsync(
        IReadOnlyList<MediaCandidate> candidates, MediaRequest request, CancellationToken ct = default)
    {
        foreach (var (provider, resolved) in Capable(candidates, request, MediaDelivery.Job))
        {
            if (provider is not IMediaJobProvider job) continue;   // capability says Job; the type must agree
            var operation = await job.SubmitAsync(resolved, ct).ConfigureAwait(false);
            if (operation.Status != MediaOperationStatus.Failed)
                return new MediaSubmission(provider.Id, operation);
        }

        return new MediaSubmission("", new MediaOperation("", MediaOperationStatus.Failed,
            Detail: $"no capable media backend accepted a '{request.Kind}' job among " +
                $"[{string.Join(", ", candidates.Select(c => c.ProviderId))}]"));
    }

    /// <summary>Candidates that exist, are registered, and DECLARE they can serve this request/delivery —
    /// with the candidate's model applied to the request when it pins one.</summary>
    private IEnumerable<(IMediaProvider Provider, MediaRequest Request)> Capable(
        IReadOnlyList<MediaCandidate> candidates, MediaRequest request, MediaDelivery delivery)
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
