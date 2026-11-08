using Lyntai.Inference;
using Lyntai.Cortex;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

public class LlmScorerBaseTests
{
    // a concrete judge whose model/consumer come from ctor args (via the prop overrides) — the "subclass/ctor
    // sets it" path. `applies` gates whether the judge runs at all.
    private sealed class Judge(ITextClient llm, string? model = null, string consumer = "scoring", bool applies = true)
        : LlmScorerBase(llm)
    {
        public override string Id => "judge";
        public override string Name => "Judge";
        protected override string? Model => model;
        protected override string Consumer => consumer;
        protected override bool Applies(ScoreContext ctx) => applies;
        protected override string BuildJudgePrompt(ScoreContext ctx) => "judge this";
    }

    private static ScoreContext Ctx => new() { SessionId = "s", Output = "out" };

    private static FakeTextClient ClientReturning(double score)
    {
        var llm = new FakeTextClient();
        llm.Replies.Enqueue(new TextResponse(
            $$"""{"score":{{score.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""", ProviderVerdict.Ok));
        return llm;
    }

    [Fact]
    public async Task Defaults_to_the_scoring_consumer_and_the_routed_default_model()
    {
        var llm = ClientReturning(0.8);
        var result = await new Judge(llm).ScoreAsync(Ctx, default);

        Assert.Equal(0.8, result!.Score);
        var req = Assert.Single(llm.Calls);
        Assert.Equal("scoring", req.Consumer);
        Assert.Null(req.Model);                 // null = the routed default
    }

    [Fact]
    public async Task Per_scorer_model_and_consumer_thread_into_the_judge_request()
    {
        var llm = ClientReturning(0.8);
        await new Judge(llm, model: "haiku", consumer: "scoring-cheap").ScoreAsync(Ctx, default);

        var req = Assert.Single(llm.Calls);
        Assert.Equal("haiku", req.Model);       // a cheap judge routed to a cheap model
        Assert.Equal("scoring-cheap", req.Consumer);
    }

    [Theory]
    [InlineData("""{"score":1e999}""", 1.0)]      // overflows to infinity, then clamps: never an exception
    [InlineData("""{"score":-1e999}""", 0.0)]
    [InlineData("""{"score":-4,"reason":"low"}""", 0.0)]
    [InlineData("""{"score":7}""", 1.0)]
    [InlineData("""{"score":0.25,"reason":"fine"}""", 0.25)]
    public void A_verdict_is_clamped_to_the_unit_interval(string reply, double expected)
    {
        Assert.True(LlmScorerBase.TryParseVerdict(reply, out var result));
        Assert.Equal(expected, result.Score);
    }

    [Theory]
    [InlineData("""{"score":"0.5"}""")]
    [InlineData("""{"reason":"no score"}""")]
    [InlineData("not json at all")]
    public void A_reply_without_a_numeric_score_is_unusable(string reply)
    {
        Assert.False(LlmScorerBase.TryParseVerdict(reply, out _));
    }

    [Fact]
    public async Task A_non_applicable_dimension_returns_null_without_calling_the_client()
    {
        var llm = ClientReturning(0.9);
        var result = await new Judge(llm, applies: false).ScoreAsync(Ctx, default);

        Assert.Null(result);        // omitted, not recorded
        Assert.Empty(llm.Calls);    // and no tokens spent — the judge never ran
    }
}
