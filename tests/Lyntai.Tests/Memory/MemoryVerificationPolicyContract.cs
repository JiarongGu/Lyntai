using Lyntai.Memory.Verification;

namespace Lyntai.Tests.Memory;

/// <summary>Every <see cref="MemoryVerificationPolicyContract"/> fact, inherited, so each shipped verifier runs the
/// whole contract BY CONSTRUCTION — the shape <see cref="RetrievabilityPolicyContractFacts"/> uses. A driver
/// supplies a working policy and the ways its implementation can be broken; the defaulted factories are for a
/// fact that wants its own pressure (a judge naming an id past the list, or one twice).</summary>
public abstract class MemoryVerificationPolicyContractFacts
{
    /// <summary>A policy that judges.</summary>
    protected abstract IMemoryVerificationPolicy Working();

    /// <summary>A working policy pushed to return an id it was not shown.</summary>
    protected virtual IMemoryVerificationPolicy PushedPastTheCandidates() => Working();

    /// <summary>A working policy pushed to return an id twice.</summary>
    protected virtual IMemoryVerificationPolicy PushedToRepeat() => Working();

    /// <summary>A policy broken however its implementation can be broken.</summary>
    protected abstract IMemoryVerificationPolicy Failing();

    /// <summary>A policy whose dependency times out while nobody cancelled.</summary>
    protected abstract IMemoryVerificationPolicy TimingOut();

    /// <summary>A working policy whose dependency honours the caller's token.</summary>
    protected virtual IMemoryVerificationPolicy HonouringCancellation() => Working();

    [Fact] public Task Never_null() => MemoryVerificationPolicyContract.It_never_returns_null(Working());
    [Fact] public Task Ids_were_shown() =>
        MemoryVerificationPolicyContract.Every_id_it_returns_was_one_it_was_shown(PushedPastTheCandidates());
    [Fact] public Task No_duplicates() => MemoryVerificationPolicyContract.It_returns_no_duplicates(PushedToRepeat());
    [Fact] public Task Empty_candidates() => MemoryVerificationPolicyContract.An_empty_candidate_set_is_no_opinion(Working());
    [Fact] public Task Fails_open() =>
        MemoryVerificationPolicyContract.A_failing_policy_yields_NoOpinion_and_not_NothingRelevant(Failing());
    [Fact] public Task Its_own_timeout_fails_open() =>
        MemoryVerificationPolicyContract.A_policy_timing_out_on_its_own_yields_NoOpinion(TimingOut());
    [Fact] public Task Cancellation_propagates() =>
        MemoryVerificationPolicyContract.Cancellation_propagates_rather_than_becoming_no_opinion(HonouringCancellation());
}

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
/// contract is model-free even though its implementation is not.</para></summary>
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

    /// <summary><b>A policy's OWN timeout is a MODEL failure, not a cancellation</b> — so it lands on
    /// <see cref="MemoryVerification.NoOpinion"/> like any other outage. An <see cref="HttpClient"/> timeout
    /// surfaces as <see cref="TaskCanceledException"/>, which IS an <see cref="OperationCanceledException"/>,
    /// so a <c>catch (OperationCanceledException) { throw; }</c> ahead of the fail-open handler fails CLOSED
    /// on a slow model — which is the failure this seam has most often.
    /// <para>The engine-side fix is <c>docs/FIXES.md</c>, 2026-09-09; the promise belongs here, where every
    /// implementation of the seam is held to it.</para></summary>
    public static async Task A_policy_timing_out_on_its_own_yields_NoOpinion(
        IMemoryVerificationPolicy timingOut)
    {
        var verdict = await timingOut.VerifyAsync(Request());

        Assert.NotNull(verdict);
        Assert.False(verdict.Judged);
        Assert.Empty(verdict.RelevantIds);
    }

    /// <summary>Cancellation is never swallowed — the same rule the annotation seam carries, and for the same
    /// reason: fail-open covers the MODEL failing, not the caller asking to stop.
    /// <para>The other half of the fact above: the fix for one is "swallow every cancellation", which breaks
    /// this. Every SHIPPED implementation runs both, for that reason — <see cref="PolicyContractCoverageTests"/>
    /// is what keeps that true.</para></summary>
    public static async Task Cancellation_propagates_rather_than_becoming_no_opinion(
        IMemoryVerificationPolicy policy)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => policy.VerifyAsync(Request(), cancelled.Token));
    }
}
