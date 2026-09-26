using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Verification;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// A verifier is shown each candidate's <see cref="MemoryVerificationCandidate.Relevance"/>, so abstention can
/// be decided WITHOUT a model.
///
/// <para><b>Why an id and a headline are insufficient.</b> <see cref="IMemoryVerificationPolicy"/>'s own doc
/// promises "best-effort over a model-free floor", and from an id and a headline the only route to "did
/// anything answer this" is reading the text, which means an LLM such as
/// <see cref="LlmMemoryVerificationPolicy"/> — measured on that route promoting the right answer 0 of 6 times
/// at 5.4 s cold. Score distribution answers the same question with arithmetic, and the engine has the
/// numbers.</para>
///
/// <para>These pin the DATA, not a policy. The library ships no score-floor verifier, because the floor is a
/// property of the deployment's vector backend and corpus — `generic-library` rule 7: a value only the deployment
/// can know is the host's to supply.</para>
/// </summary>
public class MemoryVerificationRelevanceTests
{
    /// <summary>On SQLite, whose relevance is a rank position (<c>1 - i/n</c>), so two matches carry two
    /// DIFFERENT values. The in-process store reports <c>1</c> for every match, where a candidate wired to the
    /// literal <c>1</c> would pass both facts below.</summary>
    private static async Task<(CapturingVerification Judge, MemoryRecall Recall)> RecallWithJudge(TempDb db)
    {
        var judge = new CapturingVerification();
        var engine = new GraphMemoryEngine("graph", new SqliteMemoryGraphStore(db.Factory), seams: new GraphMemorySeams
            {
                Verification = judge,
            });

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
        using var db = new TempDb();
        var (judge, recall) = await RecallWithJudge(db);

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
        // candidates in RANK order, so the sequence must be non-increasing, must not be uniformly zero — a
        // flat zero column is exactly what a score-floor policy would read as "nothing matched" — and must
        // carry at least two distinct values, which no literal can.
        using var db = new TempDb();
        var (judge, _) = await RecallWithJudge(db);
        var scores = judge.Last!.Candidates.Select(c => c.Relevance).ToList();

        Assert.Contains(scores, s => s > 0);
        Assert.True(scores.Distinct().Count() >= 2, $"relevance is a constant: {string.Join(", ", scores)}");
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
