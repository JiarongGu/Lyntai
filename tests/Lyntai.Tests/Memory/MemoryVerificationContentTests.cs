using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Verification;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// A verifier is shown each candidate's full <see cref="MemoryVerificationCandidate.Content"/> beside its
/// headline, so a policy that needs to READ the entry is not judging a truncation.
///
/// <para><b>Why the seam was insufficient, measured.</b> A candidate carried the headline alone, and
/// <see cref="GraphMemoryOptions.HeadlineChars"/> derives that as a 120-character cut of the content. A
/// cross-encoder reranker — whose whole job is scoring the pair — therefore read the first 120 characters
/// of most candidates and SPENT 7.5 points where a perfect judge offers +7.0; given whole turns instead it
/// gained 5.0 (<c>docs/memory.md</c> §5). The engine had the text the entire time:
/// <c>SeedAsync</c> already selects the content column and <c>GraphNode.Content</c> carries it, so passing
/// it costs no extra read.</para>
///
/// <para>These pin the DATA, not a policy. Which text to read stays the policy's choice — a judge paying by
/// the token keeps using the headline, and nothing about the shipped one changes.</para>
/// </summary>
public class MemoryVerificationContentTests
{
    /// <summary>Captures the request verbatim and judges nothing, so ordering is unchanged and the only
    /// thing under test is what the engine handed over.</summary>
    private sealed class CapturingVerification : IMemoryVerificationPolicy
    {
        public MemoryVerificationRequest? Last { get; private set; }

        public Task<MemoryVerification> VerifyAsync(
            MemoryVerificationRequest request, CancellationToken ct = default)
        {
            Last = request;
            return Task.FromResult(MemoryVerification.NoOpinion);
        }
    }

    /// <summary>Comfortably past the 120-character default, so the headline is provably a cut of it.</summary>
    private static string LongContent(string marker) =>
        $"{marker} the deployment checklist covers the approval step, the rollback plan, the on-call "
        + "rotation, the schema migration order, and the sign-off that has to happen before any of it "
        + $"reaches production, with {marker} appearing again at the very end.";

    private static async Task<CapturingVerification> RecallWithJudge()
    {
        var judge = new CapturingVerification();
        var engine = new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore(), verification: judge);
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
        // engine that never truncated, and the seam's defect would be untestable.
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
