using Lyntai.Lifecycle;
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
    ILogger<ScoringVerificationPolicy>? logger = null) : IMemoryVerificationPolicy
{
    private readonly ILogger _logger = logger ?? NullLogger<ScoringVerificationPolicy>.Instance;

    /// <summary>Endorses the backend's best <see cref="ScoringVerificationOptions.EndorseCount"/> of
    /// whatever the engine showed it, or <c>NoOpinion</c> when nothing usable answered.</summary>
    /// <exception cref="OperationCanceledException">The CALLER's <paramref name="ct"/> was cancelled.</exception>
    public async Task<MemoryVerification> VerifyAsync(
        MemoryVerificationRequest request, CancellationToken ct = default)
    {
        if (request.Candidates.Count == 0) return MemoryVerification.NoOpinion;

        var backend = providers.FirstOrDefault(p => p.IsAvailable
            && p.Capabilities.Supports(ProviderKinds.Score, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        if (backend is null)
        {
            _logger.LogDebug("no backend produces {Kind}; reporting NoOpinion", ProviderKinds.Score);
            return MemoryVerification.NoOpinion;
        }

        // CONTENT, falling back to the headline only when the engine supplied none. A headline is a
        // truncation and scoring a fragment cost this arm 13 points (D108).
        var documents = request.Candidates.Select(c => c.Content ?? c.Headline).ToList();

        IReadOnlyList<double> scores;
        try
        {
            scores = await backend.ScoreAsync(request.Query, documents, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "{Id} did not score usably; reporting NoOpinion", backend.Id);
            return MemoryVerification.NoOpinion;
        }

        // A short or long answer is a malformed one: pairing by position is only sound when the counts agree,
        // and a silent truncation would endorse the wrong candidates rather than none.
        if (scores.Count != documents.Count)
        {
            _logger.LogDebug("{Id} scored {Got} of {Sent} documents; reporting NoOpinion",
                backend.Id, scores.Count, documents.Count);
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
