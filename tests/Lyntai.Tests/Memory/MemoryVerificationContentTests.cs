using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Verification;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// A verifier is shown each candidate's full <see cref="MemoryVerificationCandidate.Content"/> beside its
/// headline, so a policy that needs to READ the entry is not judging a truncation.
///
/// <para><b>Why the headline alone is insufficient, measured.</b>
/// <see cref="GraphMemoryOptions.HeadlineChars"/> derives the headline as a 120-character cut of the
/// content, so a cross-encoder reranker — whose whole job is scoring the pair — shown only headlines reads
/// the first 120 characters of most candidates and SPENDS 7.5 points where a perfect judge offers +7.0;
/// given whole turns it gains 5.0 (<c>docs/memory-measurements.md</c> §5). Passing the content costs no
/// extra read: <c>SeedAsync</c> already selects the content column and <c>GraphNode.Content</c> carries
/// it.</para>
///
/// <para>These pin the DATA, not a policy. Which text to read stays the policy's choice — a judge paying by
/// the token keeps using the headline, and nothing about the shipped one changes.</para>
/// </summary>
public class MemoryVerificationContentTests
{
    /// <summary>Comfortably past the 120-character default, so the headline is provably a cut of it.</summary>
    private static string LongContent(string marker) =>
        $"{marker} the deployment checklist covers the approval step, the rollback plan, the on-call "
        + "rotation, the schema migration order, and the sign-off that has to happen before any of it "
        + $"reaches production, with {marker} appearing again at the very end.";

    private static async Task<CapturingVerification> RecallWithJudge()
    {
        var judge = new CapturingVerification();
        var engine = new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                Verification = judge,
            });
        await engine.RememberAsync(new MemoryWrite("t", "s", LongContent("marker7")));

        await engine.RecallAsync(new MemoryQuery("t", "s", "marker7", 10));
        return judge;
    }

    [Fact]
    public async Task The_verifier_is_shown_the_full_content_not_the_truncated_headline()
    {
        var judge = await RecallWithJudge();

        var candidate = Assert.Single(judge.Last!.Candidates);
        Assert.Equal(LongContent("marker7"), candidate.Content);
    }

    [Fact]
    public async Task The_headline_it_is_shown_beside_really_is_a_truncation()
    {
        // The premise of the test above: without this, "content equals the write" would also pass on an
        // engine that never truncated, and the reason for the seam would be untestable.
        var judge = await RecallWithJudge();

        var candidate = Assert.Single(judge.Last!.Candidates);
        Assert.True(candidate.Headline.Length < candidate.Content!.Length,
            $"headline ({candidate.Headline.Length}) should be a cut of content ({candidate.Content.Length})");
    }

    [Fact]
    public void A_hand_built_candidate_may_omit_content_and_reports_null_rather_than_empty()
    {
        // null is "nobody supplied one", which a policy must be able to tell from a genuinely empty entry —
        // the same distinction MemoryVerification draws between no opinion and an empty endorsement.
        var candidate = new MemoryVerificationCandidate("1", "a headline");

        Assert.Null(candidate.Content);
    }
}

/// <summary>
/// A verifier's own TIMEOUT must not fail the recall. The seam is documented fail-open — the engine logs and
/// returns <c>NoOpinion</c> — and it must still let a caller's cancellation propagate. An
/// <see cref="HttpClient"/> timeout surfaces as <see cref="TaskCanceledException"/>, which IS an
/// <see cref="OperationCanceledException"/>, so rethrowing every one turns the single most likely failure of
/// a model-backed policy — a slow model — into a failed recall.
///
/// <para>The distinction the engine draws is the standard one: rethrow only when the CALLER's token is
/// actually cancelled (<c>docs/FIXES.md</c>, 2026-09-09).</para>
/// </summary>
public class MemoryVerificationTimeoutTests
{
    private sealed class TimesOut : IMemoryVerificationPolicy
    {
        public Task<MemoryVerification> VerifyAsync(
            MemoryVerificationRequest request, CancellationToken ct = default) =>
            // exactly what HttpClient throws on its own timeout: a cancellation nobody asked for
            throw new TaskCanceledException("the request was canceled due to the configured HttpClient.Timeout");
    }

    /// <summary>The caller stops MID-RECALL, and the exception is MARKED. Both halves are what make the
    /// caller-cancel test able to discriminate: <c>RecallAsync</c> checks the token before anything else, so
    /// a PRE-cancelled one never reaches this seam at all and the store throws on its own — a bare
    /// <c>ThrowsAnyAsync</c> therefore passes whatever the seam does, including under the wrong fix.</summary>
    private sealed class CancelsWithMarker(CancellationTokenSource cts) : IMemoryVerificationPolicy
    {
        public const string Marker = "the verifier saw the caller's cancellation";

        public Task<MemoryVerification> VerifyAsync(
            MemoryVerificationRequest request, CancellationToken ct = default)
        {
            cts.Cancel();
            throw new OperationCanceledException(Marker, cts.Token);
        }
    }

    private static async Task<MemoryRecall> RecallWithTimingOutJudge(CancellationToken ct = default)
    {
        var engine = new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                Verification = new TimesOut(),
            });
        await engine.RememberAsync(new MemoryWrite("t", "s", "marker9 the deployment checklist"), ct);
        return await engine.RecallAsync(new MemoryQuery("t", "s", "marker9", 10), ct);
    }

    [Fact]
    public async Task A_verifier_timing_out_degrades_to_no_opinion_rather_than_failing_the_recall()
    {
        var recall = await RecallWithTimingOutJudge();

        Assert.NotEmpty(recall.Items);
    }

    [Fact]
    public async Task A_CALLER_cancelling_still_propagates_FROM_THE_VERIFIER()
    {
        // The other half, or the fix would be "swallow every cancellation" — which would make a cancelled
        // recall look like a successful one.
        using var cts = new CancellationTokenSource();
        var engine = new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                Verification = new CancelsWithMarker(cts),
            });
        await engine.RememberAsync(new MemoryWrite("t", "s", "marker9 the deployment checklist"));

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RecallAsync(new MemoryQuery("t", "s", "marker9", 10), cts.Token));

        Assert.Equal(CancelsWithMarker.Marker, thrown.Message);
    }
}
