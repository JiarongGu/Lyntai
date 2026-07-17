using Lyntai.Cortex;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

public class PairwiseComparerTests
{
    private static LlmReply Json(string winner) =>
        new($$"""{"winner":"{{winner}}","reason":"because"}""", LlmVerdict.Ok);

    [Fact]
    public void Parse_reads_the_winner_and_reason()
    {
        Assert.True(LlmPairwiseComparer.TryParse("""{"winner":"a","reason":"clearer"}""", out var r));
        Assert.Equal(PairwiseWinner.A, r.Winner);
        Assert.Equal("clearer", r.Reason);

        Assert.True(LlmPairwiseComparer.TryParse("Sure:\n```json\n{\"winner\":\"TIE\"}\n```", out var t));
        Assert.Equal(PairwiseWinner.Tie, t.Winner); // case-insensitive, fence-tolerant
    }

    [Fact]
    public async Task Single_pass_returns_the_judge_pick()
    {
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(Json("a"));
        var comparer = new LlmPairwiseComparer(llm, mitigatePositionBias: false);

        var result = await comparer.CompareAsync("q", "answer A", "answer B");

        Assert.Equal(PairwiseWinner.A, result.Winner);
        Assert.Single(llm.Calls);
    }

    [Fact]
    public async Task Position_bias_mitigation_confirms_a_consistent_winner()
    {
        // forward call picks slot A (=outputA); swapped call picks slot B (=outputA again) → consistent A
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(Json("a")); // forward: A wins
        llm.Replies.Enqueue(Json("b")); // swapped: slot B wins, which is outputA → still A
        var comparer = new LlmPairwiseComparer(llm); // mitigation on by default

        var result = await comparer.CompareAsync("q", "answer A", "answer B");

        Assert.Equal(PairwiseWinner.A, result.Winner);
        Assert.Equal(2, llm.Calls.Count);
    }

    [Fact]
    public async Task Position_bias_disagreement_becomes_a_tie()
    {
        // both passes pick "slot A" → the judge just favors whatever is first (position bias) → Tie
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(Json("a")); // forward: slot A (=outputA)
        llm.Replies.Enqueue(Json("a")); // swapped: slot A (=outputB) → the two disagree on the real output
        var comparer = new LlmPairwiseComparer(llm);

        var result = await comparer.CompareAsync("q", "answer A", "answer B");

        Assert.Equal(PairwiseWinner.Tie, result.Winner);
        Assert.Contains("position-bias", result.Reason);
    }

    [Fact]
    public async Task A_failed_judge_verdict_is_a_tie()
    {
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "down"));
        var comparer = new LlmPairwiseComparer(llm, mitigatePositionBias: false);

        var result = await comparer.CompareAsync("q", "a", "b");

        Assert.Equal(PairwiseWinner.Tie, result.Winner);
    }
}
