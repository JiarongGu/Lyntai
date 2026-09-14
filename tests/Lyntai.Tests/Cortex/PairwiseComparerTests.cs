using Lyntai.Lifecycle;
using Lyntai.Cortex;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

public class PairwiseComparerTests
{
    private static LlmReply Json(string winner) =>
        new($$"""{"winner":"{{winner}}","reason":"because"}""", ProviderVerdict.Ok);

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
        llm.Replies.Enqueue(new LlmReply("", ProviderVerdict.Failed, Detail: "down"));
        var comparer = new LlmPairwiseComparer(llm, mitigatePositionBias: false);

        var result = await comparer.CompareAsync("q", "a", "b");

        Assert.Equal(PairwiseWinner.Tie, result.Winner);   // Tie stays: it is the safe neutral
        Assert.False(result.Judged);                        // …but it was not a JUDGEMENT
    }

    // ---- "the judge said tie" vs "the judge never answered" --------------------------------------------
    // Both were PairwiseWinner.Tie and nothing typed told them apart, which is the conflation
    // MemoryVerification.Judged exists to prevent one subsystem over: a model outage read as a substantive
    // verdict. Winner is unchanged in every case below — only Judged is new.

    [Fact]
    public async Task An_unparseable_reply_is_NOT_a_judgement_even_though_the_call_succeeded()
    {
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(new LlmReply("I'd rather not pick, sorry.", ProviderVerdict.Ok));
        var comparer = new LlmPairwiseComparer(llm, mitigatePositionBias: false);

        var result = await comparer.CompareAsync("q", "a", "b");

        Assert.Equal(PairwiseWinner.Tie, result.Winner);
        Assert.False(result.Judged);
    }

    /// <summary>The positive control for the pair above: without it, an implementation that simply reported
    /// <c>Judged = false</c> for every Tie would pass every other test here.</summary>
    [Fact]
    public async Task A_judge_that_genuinely_answers_TIE_IS_a_judgement()
    {
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(Json("tie"));
        var comparer = new LlmPairwiseComparer(llm, mitigatePositionBias: false);

        var result = await comparer.CompareAsync("q", "a", "b");

        Assert.Equal(PairwiseWinner.Tie, result.Winner);
        Assert.True(result.Judged, "the model answered, and 'neither is better' is a real verdict");
    }

    /// <summary>A position-bias disagreement is still a REAL judgement: the model answered twice and the
    /// Tie is this library's considered reading of two genuine verdicts, not an absence of one. Judged
    /// means "the model gave a usable answer", never "the answer was decisive".</summary>
    [Fact]
    public async Task A_position_bias_disagreement_is_still_a_judgement()
    {
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(Json("a"));
        llm.Replies.Enqueue(Json("a"));   // both pick the first SLOT → the two disagree on the real output
        var comparer = new LlmPairwiseComparer(llm);

        var result = await comparer.CompareAsync("q", "answer A", "answer B");

        Assert.Equal(PairwiseWinner.Tie, result.Winner);
        Assert.True(result.Judged);
    }

    /// <summary>…but a pass that did not answer POISONS the combined result, because then there were never
    /// two verdicts to compare. Without this the two-pass path could report a confident judgement built on
    /// one real answer and one failure.</summary>
    [Fact]
    public async Task A_two_pass_run_where_EITHER_pass_failed_is_not_a_judgement()
    {
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(Json("a"));
        llm.Replies.Enqueue(new LlmReply("", ProviderVerdict.Failed, Detail: "down"));
        var comparer = new LlmPairwiseComparer(llm);

        var result = await comparer.CompareAsync("q", "answer A", "answer B");

        Assert.False(result.Judged);
    }

    /// <summary>A BYO <see cref="IPairwiseComparer"/> written before this existed constructs the record
    /// positionally and must keep meaning "I judged" — which is why the property defaults to true and is
    /// not a positional parameter (that would have changed the ctor and Deconstruct, breaking D70).</summary>
    [Fact]
    public void A_result_constructed_the_old_way_still_means_JUDGED()
    {
        Assert.True(new PairwiseResult(PairwiseWinner.A).Judged);
        Assert.True(new PairwiseResult(PairwiseWinner.Tie, "close call").Judged);
    }
}
