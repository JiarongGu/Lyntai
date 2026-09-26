using System.Reflection;
using Lyntai.Cortex;
using Lyntai.Inference;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

/// <summary>
/// <see cref="LlmScorerBase.Applies"/> is PROTECTED, and a protected member cannot implicitly implement an
/// interface member — so without the explicit <c>bool IScorer.Applies(…) =&gt; Applies(…)</c> forwarder, every
/// judge answers the INTERFACE-level call with <see cref="IScorer"/>'s default implementation (always true),
/// whatever the subclass says.
/// </summary>
public class ScorerAppliesTests
{
    private sealed class Judge(ITextClient llm, bool applies) : LlmScorerBase(llm)
    {
        public override string Id => "judge";
        public override string Name => "Judge";
        protected override bool Applies(ScoreContext ctx) => applies;
        protected override string BuildJudgePrompt(ScoreContext ctx) => "judge this";
    }

    // A gate that is itself a bug. A predicate is contractually pure and cheap, which is why ScoringService
    // calls it OUTSIDE its per-scorer try — a broken one must surface, not be logged away.
    private sealed class ThrowingGateJudge(ITextClient llm) : LlmScorerBase(llm)
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
        // A caller running its own scorer loop asks the INTERFACE, so that is where an opt-out must be heard.
        IScorer gated = new Judge(new FakeTextClient(), applies: false);
        IScorer open = new Judge(new FakeTextClient(), applies: true);

        Assert.False(gated.Applies(Ctx));
        Assert.True(open.Applies(Ctx));
    }

    [Fact]
    public async Task A_gated_judge_still_spends_nothing_and_records_nothing()
    {
        // ScoreAsync re-checks the gate as its first line, so an opted-out judge spends no token and records
        // no result whichever gate a caller asks.
        var llm = new FakeTextClient();
        var service = new ScoringService([new Judge(llm, applies: false)]);

        var results = await service.EvaluateAsync(Ctx, persist: false);

        Assert.Empty(results);
        Assert.Empty(llm.Calls);
    }

    [Fact]
    public async Task A_throwing_gate_surfaces_out_of_EvaluateAsync_instead_of_being_swallowed()
    {
        // The gate runs where ScoringService deliberately puts it, outside the per-scorer try, so a buggy
        // predicate is a bug. Thrown from ScoreAsync's own re-check INSIDE the try, it would be logged and
        // skipped fail-open, indistinguishable from a dimension that legitimately did not apply.
        var service = new ScoringService([new ThrowingGateJudge(new FakeTextClient())]);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EvaluateAsync(Ctx, persist: false));

        Assert.Equal("gate bug", ex.Message);
    }

    [Fact]
    public void The_forwarder_is_explicit_so_Applies_stays_protected()
    {
        // Promoting the protected member to public would be a compile break for every
        // `protected override bool Applies(…)` in a consumer's judge. The same assertion pins that the
        // forwarder adds nothing to the public API surface (an explicit interface implementation is private).
        Assert.Null(typeof(LlmScorerBase).GetMethod("Applies", BindingFlags.Public | BindingFlags.Instance));

        var gate = typeof(LlmScorerBase).GetMethod("Applies", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(gate);
        Assert.True(gate.IsFamily);   // protected, and still virtual for the subclass to override
        Assert.True(gate.IsVirtual);
    }
}
