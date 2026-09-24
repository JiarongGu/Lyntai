using Lyntai.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory.Verification;

/// <summary>Verifies a recall by asking a backend that produces <see cref="ProviderKinds.Score"/> to rank
/// the candidates, and endorsing its best
/// <see cref="ScoringVerificationOptions.EndorseCount"/>.
///
/// <para><b>It knows no wire format, and that is the point.</b> This policy and the cross-encoder HTTP
/// client were ONE class in a provider package until <c>docs/DECISIONS.md</c> <b>D140</b> — so the transport
/// was reachable by nothing else, and a memory decision (which field of a candidate to send, how many to
/// endorse) shipped inside an adapter. Any backend declaring <see cref="ProviderKinds.Score"/> serves this
/// now, including a local one that never speaks HTTP.</para>
///
/// <para><b>It is FAIL-OPEN, and the provider is not.</b> A scoring backend throws rather than returning a
/// degraded answer, because there is no score meaning "I could not". Here a missing opinion is a legitimate
/// outcome — <see cref="MemoryVerification.NoOpinion"/> leaves the engine's own ranking untouched — so this
/// catches and reports nothing rather than failing the recall. **The caller's own cancellation is
/// re-thrown**, tested as <c>ct.IsCancellationRequested</c> and never by the exception's type, because a
/// provider's own timeout arrives as the same type.</para></summary>
public sealed class ScoringVerificationPolicy(
    IEnumerable<IModelProvider> providers,
    ScoringVerificationOptions config,
    ILogger<ScoringVerificationPolicy>? logger = null,
    IProviderRouterFactory? routing = null) : IMemoryVerificationPolicy
{
    private readonly ILogger _logger = logger ?? NullLogger<ScoringVerificationPolicy>.Instance;

    /// <summary>The backends this policy may ask — every registered one, or just the one
    /// <see cref="ScoringVerificationOptions.ProviderId"/> names. Narrowed ONCE at composition so a name
    /// matching nothing throws here; availability is still re-read per recall, because a backend can go down
    /// between calls and a name cannot.</summary>
    private readonly IReadOnlyList<IModelProvider> _backends = Selectable(providers, config);

    private static IReadOnlyList<IModelProvider> Selectable(
        IEnumerable<IModelProvider> providers, ScoringVerificationOptions config)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(config);

        var all = providers.ToList();
        if (string.IsNullOrWhiteSpace(config.ProviderId)) return all;

        var named = all.FirstOrDefault(
            p => string.Equals(p.Id, config.ProviderId, StringComparison.OrdinalIgnoreCase));
        if (named is null)
            throw new InvalidOperationException(
                $"{nameof(ScoringVerificationOptions)}.{nameof(ScoringVerificationOptions.ProviderId)} names "
                + $"'{config.ProviderId}', which is not a registered backend "
                + $"({(all.Count == 0 ? "(none)" : string.Join(", ", all.Select(p => p.Id)))}). A name the "
                + "seam cannot resolve would leave every recall unverified and report nothing.");

        // Checked here rather than left to the per-call capability filter, which would skip it in silence:
        // naming the chat backend is the likelier typo of the two, since the id really does exist.
        if (!named.Capabilities.Produces.Contains(ProviderKinds.Score, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"{nameof(ScoringVerificationOptions)}.{nameof(ScoringVerificationOptions.ProviderId)} names "
                + $"'{named.Id}', which does not declare {ProviderKinds.Score} — it produces "
                + $"{(named.Capabilities.Produces.Count == 0 ? "nothing" : string.Join(", ", named.Capabilities.Produces))}. "
                + "Name a reranker, or drop the setting to take the first backend that scores.");

        return [named];
    }

    /// <summary>Endorses the backend's best <see cref="ScoringVerificationOptions.EndorseCount"/> of
    /// whatever the engine showed it, or <c>NoOpinion</c> when nothing usable answered.</summary>
    /// <exception cref="OperationCanceledException">The CALLER's <paramref name="ct"/> was cancelled.</exception>
    public async Task<MemoryVerification> VerifyAsync(
        MemoryVerificationRequest request, CancellationToken ct = default)
    {
        if (request.Candidates.Count == 0) return MemoryVerification.NoOpinion;

        // ROUTED since D153, where this used to take FirstOrDefault and stop. A second registered reranker
        // is now a failover rather than decoration, and a rate-limited one is benched instead of being asked
        // again on the next recall. The seam stays FAIL-OPEN either way: a non-Ok verdict reports NoOpinion.
        // NO logger passed on purpose. The router warns per failed attempt, which is right for a call a
        // consumer is waiting on and wrong here: this seam is FAIL-OPEN and runs on every recall, so a
        // transport blip would become per-recall noise at Warning. The outcome is logged below at debug,
        // carrying the verdict and the backend's own words, which is what a reader of this seam needs.
        var router = routing?.For<ScoreRequest, ScoreResponse>(
                _backends, ScoreResponse.Failure,
                c => c.Supports(ProviderKinds.Score, ProviderOperation.Complete, accepts: ProviderKinds.Text))
            ?? new ProviderRouter<ScoreRequest, ScoreResponse>(
                _backends, ScoreResponse.Failure,
                c => c.Supports(ProviderKinds.Score, ProviderOperation.Complete, accepts: ProviderKinds.Text));

        if (!router.CanServe())
        {
            _logger.LogDebug("no backend produces {Kind}; reporting NoOpinion", ProviderKinds.Score);
            return MemoryVerification.NoOpinion;
        }

        // CONTENT, falling back to the headline only when the engine supplied none. A headline is a
        // truncation and scoring a fragment cost this arm 13 points (D108).
        var documents = request.Candidates.Select(c => c.Content ?? c.Headline).ToList();

        ScoreResponse response;
        try
        {
            response = await router.CallAsync(
                new ScoreRequest(request.Query, documents, Consumer: ProviderConsumers.Memory), ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            // The router classifies a backend's own throw into a verdict, so reaching here means something
            // outside a backend faulted. Fail-open, as the contract says.
            _logger.LogDebug(ex, "scoring verification faulted; reporting NoOpinion");
            return MemoryVerification.NoOpinion;
        }

        if (!response.IsOk)
        {
            // a transient fault is per-recall noise; one this recall will meet again — an input over the
            // model's window, a rejected key — is a defect, and fail-open must not hide it
            _logger.Log(response.Verdict.IsTransient() ? LogLevel.Debug : LogLevel.Warning,
                "no backend scored usably ({Verdict}: {Detail}); reporting NoOpinion",
                response.Verdict, response.Detail);
            return MemoryVerification.NoOpinion;
        }

        var scores = response.Scores;

        // A short or long answer is a malformed one: pairing by position is only sound when the counts agree,
        // and a silent truncation would endorse the wrong candidates rather than none.
        if (scores.Count != documents.Count)
        {
            // No backend id here on purpose: routing may have tried several, and naming the last one would
            // point a reader at whichever happened to answer rather than at the arity fault itself.
            _logger.LogDebug("a scoring backend returned {Got} of {Sent} documents; reporting NoOpinion",
                scores.Count, documents.Count);
            return MemoryVerification.NoOpinion;
        }

        var ranked = scores
            .Select((score, index) => (Index: index, Score: score))
            .OrderByDescending(s => s.Score)
            .ToList();

        var ids = ranked
            .Take(Math.Max(0, config.EndorseCount))
            .Select(s => request.Candidates[s.Index].Id)
            .ToList();

        // EVERY scored candidate, not just the endorsed ones: the rejected scores are what a caller needs
        // to read a margin, and this policy had been computing and discarding them.
        var byId = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var (index, score) in ranked)
            byId[request.Candidates[index].Id] = score;

        return new MemoryVerification(ids) { Scores = byId };
    }
}
