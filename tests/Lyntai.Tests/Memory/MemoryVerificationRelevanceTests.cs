using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Verification;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// A verifier is shown each candidate's <see cref="MemoryVerificationCandidate.Relevance"/>, so abstention can
/// be decided WITHOUT a model.
///
/// <para><b>Why the seam was insufficient.</b> <see cref="IMemoryVerificationPolicy"/>'s own doc promises
/// "best-effort over a model-free floor", and a candidate carried an id and a headline — from which the only
/// route to "did anything answer this" is reading the text, which means an LLM. The single shipped
/// implementation is <see cref="LlmMemoryVerificationPolicy"/>, and an adopter measuring that route recorded a
/// judge promoting the right answer 0 of 6 times at 5.4 s cold. Score distribution answers the same question
/// with arithmetic; the engine had the numbers and did not pass them.</para>
///
/// <para>These pin the DATA, not a policy. The library ships no score-floor verifier, because the floor is a
/// property of the deployment's embedder and corpus — `generic-library` rule 7: a value only the deployment
/// can know is the host's to supply.</para>
/// </summary>
public class MemoryVerificationRelevanceTests
{
    /// <summary>Captures the request verbatim and judges nothing, so ordering is unchanged and the only thing
    /// under test is what the engine handed over.</summary>
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

    private static async Task<(CapturingVerification Judge, MemoryRecall Recall)> RecallWithJudge()
    {
        var judge = new CapturingVerification();
        var engine = new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore(), verification: judge);

        foreach (var text in new[]
        {
            "the deploy pipeline needs approval before release",
            "the deploy pipeline runs migrations first",
            "unrelated note about the office kettle",
        })
        {
            await engine.RememberAsync(new MemoryWrite("t", "s", text));
        }

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "deploy pipeline"));
        return (judge, recall);
    }

    [Fact]
    public async Task Every_candidate_carries_the_relevance_the_recall_computed()
    {
        var (judge, recall) = await RecallWithJudge();

        Assert.NotNull(judge.Last);
        Assert.NotEmpty(judge.Last!.Candidates);

        // The judge sees the same numbers the caller will, keyed by the id it must echo back.
        var byId = recall.Items.ToDictionary(
            i => i.Reference.Id, i => i.Relevance, StringComparer.Ordinal);
        foreach (var candidate in judge.Last.Candidates)
        {
            Assert.True(byId.TryGetValue(candidate.Id, out var relevance),
                $"candidate {candidate.Id} is not among the returned items");
            Assert.Equal(relevance, candidate.Relevance, 6);
        }
    }

    [Fact]
    public async Task Relevance_is_not_a_constant_placeholder()
    {
        // A field wired to a literal would satisfy the test above on every row. The verifier is shown
        // candidates in RANK order, so the sequence must be non-increasing and must not be uniformly zero —
        // a flat zero column is exactly what a score-floor policy would read as "nothing matched".
        var (judge, _) = await RecallWithJudge();
        var scores = judge.Last!.Candidates.Select(c => c.Relevance).ToList();

        Assert.Contains(scores, s => s > 0);
        for (var i = 1; i < scores.Count; i++)
        {
            Assert.True(scores[i] <= scores[i - 1] + 1e-9,
                $"candidates are not in rank order: {string.Join(", ", scores)}");
        }
    }

    [Fact]
    public void A_candidate_defaults_to_zero_relevance_so_a_hand_built_request_still_compiles()
    {
        // The parameter is defaulted for the same reason every other addition here is: a BYO verifier's own
        // tests construct these, and an unavoidable break would be paid by every implementer for a field most
        // of them ignore. Zero is the honest default — "no score was supplied", which is what
        // MemoryItem.Relevance already reports for an authoritative fact the query did not match.
        var candidate = new MemoryVerificationCandidate("7", "a headline");

        Assert.Equal(0, candidate.Relevance);
    }
}
