using System.Globalization;
using Lyntai.Memory.Ranking;
using Microsoft.Extensions.Logging;

namespace Lyntai.Memory.Verification;

/// <summary>How the graph engine asks a verifier and folds its one bit into a ranking — the Verification
/// domain's half of a recall.</summary>
internal static class MemoryVerdicts
{
    /// <summary>Ask <paramref name="policy"/> which of <paramref name="scored"/> answered the query, fail-open in
    /// every direction.
    /// <para><b>Any failure yields <see cref="MemoryVerification.NoOpinion"/>, never "nothing was
    /// relevant".</b> No opinion reinforces normally, while an empty judged verdict teaches the engine the recall
    /// failed — collapsing them would let a model outage silently unlearn the whole corpus.</para></summary>
    internal static async Task<MemoryVerification> AskAsync(IMemoryVerificationPolicy? policy, string query,
        IReadOnlyList<RankedMemory> scored, ILogger logger, string engine, CancellationToken ct)
    {
        if (policy is null) return MemoryVerification.NoOpinion;

        try
        {
            // Relevance is the value the caller will see, so a policy can judge from the score distribution;
            // Content rides along because the store already read it — which to read is the policy's choice.
            var request = new MemoryVerificationRequest(query,
                [.. scored.Select(x => new MemoryVerificationCandidate(
                    x.Candidate.Node.Id.ToString(CultureInfo.InvariantCulture),
                    x.Candidate.Node.Headline,
                    x.Candidate.Node.Relevance)
                {
                    Content = x.Candidate.Node.Content,
                })]);

            return await policy.VerifyAsync(request, ct).ConfigureAwait(false) ?? MemoryVerification.NoOpinion;
        }
        // Only the CALLER's cancellation propagates: a policy's own timeout arrives as a
        // TaskCanceledException, which IS an OperationCanceledException, and must fail open like any fault.
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "memory verification failed for {Engine}; reinforcing what was returned", engine);
            return MemoryVerification.NoOpinion;
        }
    }

    /// <summary>Fold the verifier's one bit into <paramref name="ordinary"/>: a judged-relevant candidate is
    /// PROMOTED ahead of the rest, each group keeping the policy's relative order — the verifier says which
    /// entries answered, never a total ordering. <paramref name="combination"/> decides whether that promotion
    /// is absolute (<see cref="MemoryVerdictCombination.Partition"/>) or competes on rank with the policy's own
    /// order (<see cref="MemoryVerdictCombination.Fuse"/>).</summary>
    internal static List<RankedMemory> Apply(List<RankedMemory> ordinary, MemoryVerification verdict,
        MemoryVerdictCombination combination)
    {
        if (!verdict.Judged || verdict.RelevantIds.Count == 0) return ordinary;

        var relevant = verdict.RelevantIds.ToHashSet(StringComparer.Ordinal);
        bool IsRelevant(RankedMemory r) =>
            relevant.Contains(r.Candidate.Node.Id.ToString(CultureInfo.InvariantCulture));

        // Both arms start from the SAME promoted ordering, which is what makes Fuse a blend of the two rankings
        // rather than a third one.
        var promoted = new List<RankedMemory>(ordinary.Count);
        promoted.AddRange(ordinary.Where(IsRelevant));
        promoted.AddRange(ordinary.Where(r => !IsRelevant(r)));

        return combination == MemoryVerdictCombination.Fuse ? Fuse(ordinary, promoted) : promoted;
    }

    /// <summary>Blends the ranking's order with the verdict's by reciprocal rank, so an endorsement moves a
    /// candidate up without entitling it to the page.
    /// <para><b>Every candidate carries a verdict rank — an unendorsed one is ranked LAST, never unranked.</b>
    /// Scoring absence as zero would make the worst endorsement outscore the best non-endorsement at every
    /// rank, silently reproducing the partition this is an alternative to.</para></summary>
    /// <param name="byPolicy">The ranking's own order.</param>
    /// <param name="byVerdict">The same items, endorsed-first.</param>
    /// <remarks>The fusion constant is the SHIPPED DEFAULT of <see cref="ReciprocalRankFusionOptions.K"/>, read
    /// from the type, and deliberately not the consumer's configured value: this fuses the RANKING's order with
    /// the VERDICT's, a different pair of signals, measured at this constant. The verdict's weight is 1 for the
    /// same reason — no run has priced any other value.</remarks>
    private static List<RankedMemory> Fuse(IReadOnlyList<RankedMemory> byPolicy, IReadOnlyList<RankedMemory> byVerdict)
    {
        var verdictRank = new Dictionary<long, int>(byVerdict.Count);
        for (var i = 0; i < byVerdict.Count; i++) verdictRank[byVerdict[i].Candidate.Node.Id] = i + 1;

        const double weight = 1.0;
        var k = new ReciprocalRankFusionOptions().K;

        return [.. byPolicy
            .Select((r, i) => (Item: r, Index: i, Score: (1 / (k + i + 1))
                + (weight / (k + verdictRank[r.Candidate.Node.Id]))))
            // a stable tiebreak on the policy's own position: two equal scores must not reorder run to run
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Index)
            .Select(x => x.Item)];
    }
}
