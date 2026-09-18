using Lyntai.Inference;
using Lyntai.Cortex;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

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

    // ---- what code decides, so the judge is never asked ----------------------------------------------

    [Fact]
    public async Task IDENTICAL_outputs_tie_without_asking_the_judge_at_all()
    {
        // "Give the model only what it is genuinely better at. It is not better at exact comparison"
        // (`.claude/knowledge/model-decoupling.md`). Two identical strings are a string comparison, and
        // asking costs TWO calls under the default position-bias mitigation.
        var llm = new FakeLlmClient();
        var comparer = new LlmPairwiseComparer(llm);

        var result = await comparer.CompareAsync("q", "the same answer", "the same answer");

        Assert.Equal(PairwiseWinner.Tie, result.Winner);
        Assert.Empty(llm.Calls);
    }

    [Fact]
    public async Task An_identical_pair_is_JUDGED_because_a_tie_is_the_CORRECT_verdict_not_an_absent_one()
    {
        // Judged=false means "no verdict was available". Here one is, and it is certain — collapsing the
        // two would make a deterministic answer read as a judge outage.
        var llm = new FakeLlmClient();

        var result = await new LlmPairwiseComparer(llm).CompareAsync("q", "same", "same");

        Assert.True(result.Judged);
    }

    [Fact]
    public async Task A_judge_asked_about_identical_outputs_can_pick_a_WINNER_which_is_simply_wrong()
    {
        // The reason this is a correctness fix and not only a saving. Position bias is a documented failure
        // mode, and on identical text there is no signal to overcome it: a judge that answers "a" has
        // returned a false verdict, and the two-pass check cannot catch it because BOTH passes see the same
        // two strings. Wired through the single-pass path so the fake's scripted "a" would be believed.
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(Json("a"));

        var result = await new LlmPairwiseComparer(llm, mitigatePositionBias: false)
            .CompareAsync("q", "identical", "identical");

        Assert.Equal(PairwiseWinner.Tie, result.Winner);   // code, not the scripted "a"
        Assert.Empty(llm.Calls);
    }

    [Fact]
    public async Task Outputs_differing_only_in_WHITESPACE_still_ask_the_judge()
    {
        // The short-circuit is ORDINAL equality and deliberately nothing looser. Whether trailing space
        // matters is a judgement about the caller's domain — a formatting eval would say it does — so code
        // refuses to make it and the model is still asked.
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(Json("a"));

        await new LlmPairwiseComparer(llm, mitigatePositionBias: false).CompareAsync("q", "answer", "answer ");

        Assert.Single(llm.Calls);
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

    /// <summary>The COMPOSITION-ROOT route to a cheap judge, end to end.
    ///
    /// <para><b>It exists to pin a claim `docs/model-tasks.md` §5 makes and nothing tested.</b> This seam
    /// has no `ClientName` option — unlike the two memory seams, which have one because they also suppress
    /// reasoning on the request — and §5 says that is deliberate rather than a gap: "the shipped scorer,
    /// comparer and tool loop each take a client on a public constructor, and the container registrations
    /// are try-add, so registering your own instance first wins." A documented path with no test is the
    /// shape `pitfalls.md` records as how a documented knob stops being wired.</para></summary>
    [Fact]
    public async Task A_cheap_judge_is_reached_by_registering_one_over_a_NAMED_client()
    {
        var asked = new List<string>();
        LlmReply Verdict(string id) { asked.Add(id); return Json("a"); }

        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddBridgeProvider("big", (_, _) => Task.FromResult(Verdict("big")));
            b.AddBridgeProvider("cheap", (_, _) => Task.FromResult(Verdict("cheap")));
            b.UseDefaultCandidates("big");
            b.AddLlmClient("judge", c => c.UseProviders("cheap"));

            // The documented route: resolve the factory, ask it for the name you want, hand it in. TryAdd in
            // RegisterCortex means this registration — made inside the callback, which runs first — wins.
            b.Services.AddSingleton<IPairwiseComparer>(sp => new LlmPairwiseComparer(
                sp.GetRequiredService<ILlmClientFactory>().Get("judge"), mitigatePositionBias: false));
        });

        using var provider = services.BuildServiceProvider();
        var result = await provider.GetRequiredService<IPairwiseComparer>().CompareAsync("q", "a", "b");

        Assert.Equal(PairwiseWinner.A, result.Winner);
        Assert.Equal(["cheap"], asked);   // the app's default backend was never asked to judge
    }

    /// <summary>The positive control: WITHOUT that registration the comparer runs on the default client, so
    /// the test above is measuring the named client rather than a fixture that could only ever answer once.</summary>
    [Fact]
    public async Task Registering_NOTHING_leaves_the_judge_on_the_applications_default_backend()
    {
        var asked = new List<string>();
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.AddBridgeProvider("big", (_, _) => { asked.Add("big"); return Task.FromResult(Json("a")); });
            b.AddBridgeProvider("cheap", (_, _) => { asked.Add("cheap"); return Task.FromResult(Json("a")); });
            b.UseDefaultCandidates("big");
        });

        using var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<IPairwiseComparer>().CompareAsync("q", "a", "b");

        Assert.All(asked, id => Assert.Equal("big", id));
    }
}
