using Lyntai.Memory.Verification;

namespace Lyntai.Tests.Memory;

/// <summary>Policy-agnostic facts every <see cref="IMemoryVerificationPolicy"/> satisfies.
///
/// <para><b>The sharpest promise in the memory subsystem, and the one with the quietest failure.</b> A
/// verifier decides which of a recall's results actually answered the query, and the engine reinforces the
/// subset it names. So a verifier that reports <see cref="MemoryVerification.NothingRelevant"/> when it
/// simply could not decide does not return a wrong answer — it teaches the store, permanently and on every
/// recall, that nothing it holds is any use. <c>Judged: false</c> is what keeps a model outage from
/// rewriting the graph.</para>
///
/// <para>As with the annotation seam, the facts are about what happens when the model answers badly, so the
/// contract is model-free even though its implementation is not. Added 2026-08-17 by the pre-3.0 sweep
/// (archive Part 86).</para></summary>
public static class MemoryVerificationPolicyContract
{
    private static readonly MemoryVerificationCandidate[] Candidates =
    [
        new("1", "the review will be held in the small meeting room"),
        new("2", "the review covers last quarter's numbers"),
        new("3", "the small meeting room was repainted last year"),
    ];

    private static MemoryVerificationRequest Request(string query = "where is the review?") =>
        new(query, Candidates);

    /// <summary>Never null. Stated on the member's own <c>&lt;returns&gt;</c>, and relied on by the engine,
    /// which reads <c>Judged</c> without a null check.</summary>
    public static async Task It_never_returns_null(IMemoryVerificationPolicy policy)
    {
        var verdict = await policy.VerifyAsync(Request());

        Assert.NotNull(verdict);
        Assert.NotNull(verdict.RelevantIds);
    }

    /// <summary><b>IT NEVER INVENTS A RESULT.</b> "A verifier only narrows what a recall already found; it
    /// cannot add an entry." Every id it returns must be one it was shown — an id from nowhere would be
    /// reinforced by the engine against a node the recall never returned, or match no node at all and
    /// silently drop the judgement for the ones that were real.</summary>
    public static async Task Every_id_it_returns_was_one_it_was_shown(IMemoryVerificationPolicy policy)
    {
        var verdict = await policy.VerifyAsync(Request());

        var known = Candidates.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var id in verdict.RelevantIds)
            Assert.True(known.Contains(id),
                $"{policy.GetType().Name} returned id '{id}', which was not among the candidates it was shown");
    }

    /// <summary>It never repeats an id. The engine reinforces what this names; a duplicate would reinforce
    /// one entry twice for a single recall, which is exactly the positive feedback on the ranker's own prior
    /// that a verifier exists to break.</summary>
    public static async Task It_returns_no_duplicates(IMemoryVerificationPolicy policy)
    {
        var verdict = await policy.VerifyAsync(Request());

        Assert.Equal(verdict.RelevantIds.Count, verdict.RelevantIds.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary><b>FAIL-OPEN, and to <see cref="MemoryVerification.NoOpinion"/> — never to
    /// <see cref="MemoryVerification.NothingRelevant"/>.</b> The two look alike (both carry no ids) and mean
    /// opposite things: one leaves reinforcement exactly as it would have been without a verifier, the other
    /// is a judgement that the recall found nothing useful. Collapsing them means a judge that is down
    /// silently teaches the engine that every recall failed.
    /// <para>The driver supplies a policy broken in whatever way its implementation can break.</para></summary>
    public static async Task A_failing_policy_yields_NoOpinion_and_not_NothingRelevant(
        IMemoryVerificationPolicy failing)
    {
        var verdict = await failing.VerifyAsync(Request());

        Assert.NotNull(verdict);
        Assert.False(verdict.Judged,
            $"{failing.GetType().Name} reported a JUDGEMENT after failing — an outage would be recorded as "
            + "'nothing was relevant' on every recall");
        Assert.Empty(verdict.RelevantIds);
    }

    /// <summary>Nothing to judge is not a judgement. An empty candidate set reaches a verifier whenever a
    /// recall found nothing, and answering <c>Judged: true</c> there would write "this recall returned
    /// nothing useful" into the review log for a recall that returned nothing at all — a distinction the log
    /// exists to make.</summary>
    public static async Task An_empty_candidate_set_is_no_opinion(IMemoryVerificationPolicy policy)
    {
        var verdict = await policy.VerifyAsync(new MemoryVerificationRequest("anything", []));

        Assert.False(verdict.Judged);
        Assert.Empty(verdict.RelevantIds);
    }

    /// <summary>Cancellation is never swallowed — the same rule the annotation seam carries, and for the same
    /// reason: fail-open covers the MODEL failing, not the caller asking to stop.</summary>
    public static async Task Cancellation_propagates_rather_than_becoming_no_opinion(
        IMemoryVerificationPolicy policy)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => policy.VerifyAsync(Request(), cancelled.Token));
    }
}
