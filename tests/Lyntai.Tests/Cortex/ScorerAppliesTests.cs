using System.Reflection;
using Lyntai.Cortex;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

/// <summary>
/// Part 43 SCORER-APPLIES. <see cref="LlmScorerBase.Applies"/> is PROTECTED, and a protected member cannot
/// implicitly implement an interface member — so before the explicit
/// <c>bool IScorer.Applies(…) =&gt; Applies(…)</c> forwarder, every judge answered the INTERFACE-level call
/// with <see cref="IScorer"/>'s default implementation (always true), whatever the subclass said.
/// </summary>
public class ScorerAppliesTests
{
    private sealed class Judge(ILlmClient llm, bool applies) : LlmScorerBase(llm)
    {
        public override string Id => "judge";
        public override string Name => "Judge";
        protected override bool Applies(ScoreContext ctx) => applies;
        protected override string BuildJudgePrompt(ScoreContext ctx) => "judge this";
    }

    // A gate that is itself a bug. A predicate is contractually pure and cheap, which is why ScoringService
    // calls it OUTSIDE its per-scorer try — a broken one must surface, not be logged away.
    private sealed class ThrowingGateJudge(ILlmClient llm) : LlmScorerBase(llm)
    {
        public override string Id => "throwing-gate";
        public override string Name => "Throwing gate";
        protected override bool Applies(ScoreContext ctx) => throw new InvalidOperationException("gate bug");
        protected override string BuildJudgePrompt(ScoreContext ctx) => "judge this";
    }

    private static ScoreContext Ctx => new() { SessionId = "s", Output = "out" };

    [Fact]
    public void Interface_level_Applies_answers_with_the_subclass_predicate()
    {
        // The consumer-visible defect: a caller running its own scorer loop asks the INTERFACE, and used to
        // be told "applies" by every judge that had opted out.
        IScorer gated = new Judge(new FakeLlmClient(), applies: false);
        IScorer open = new Judge(new FakeLlmClient(), applies: true);

        Assert.False(gated.Applies(Ctx));
        Assert.True(open.Applies(Ctx));
    }

    [Fact]
    public async Task A_gated_judge_still_spends_nothing_and_records_nothing()
    {
        // True before AND after — ScoreAsync re-checks the gate as its first line, which is why no judge ever
        // spent a token and why the persisted results are unchanged by this fix. Pinned so the CHANGELOG's
        // "no scoring output moves" claim has a test behind it.
        var llm = new FakeLlmClient();
        var service = new ScoringService([new Judge(llm, applies: false)]);

        var results = await service.EvaluateAsync(Ctx, persist: false);

        Assert.Empty(results);
        Assert.Empty(llm.Calls);
    }

    [Fact]
    public async Task A_throwing_gate_surfaces_out_of_EvaluateAsync_instead_of_being_swallowed()
    {
        // Before: the interface gate said "applies", so ScoreAsync ran and its own re-check threw INSIDE
        // ScoringService's per-scorer try — logged and skipped fail-open, indistinguishable from a dimension
        // that legitimately did not apply. Now the gate runs where ScoringService deliberately puts it,
        // outside the try, so a buggy predicate is a bug rather than a silently dropped dimension.
        var service = new ScoringService([new ThrowingGateJudge(new FakeLlmClient())]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateAsync(Ctx, persist: false));

        Assert.Equal("gate bug", ex.Message);
    }

    [Fact]
    public void The_forwarder_is_explicit_so_Applies_stays_protected()
    {
        // The tempting alternative fix — promoting the protected member to public — is a compile break for
        // every existing `protected override bool Applies(…)` in a consumer's judge. Pin that it did not
        // happen; the same assertion pins that the forwarder adds nothing to the public API surface (an
        // explicit interface implementation is private, so the baseline does not move).
        Assert.Null(typeof(LlmScorerBase).GetMethod("Applies", BindingFlags.Public | BindingFlags.Instance));

        var gate = typeof(LlmScorerBase).GetMethod("Applies", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(gate);
        Assert.True(gate.IsFamily);   // protected, and still virtual for the subclass to override
        Assert.True(gate.IsVirtual);
    }
}
