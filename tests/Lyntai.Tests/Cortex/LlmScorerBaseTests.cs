using Lyntai.Cortex;
using Lyntai.Llm;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Cortex;

public class LlmScorerBaseTests
{
    // a concrete judge whose model/consumer come from ctor args (via the prop overrides) — the "subclass/ctor
    // sets it" path. `applies` gates whether the judge runs at all.
    private sealed class Judge(ILlmClient llm, string? model = null, string consumer = "scoring", bool applies = true)
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

    private static FakeLlmClient ClientReturning(double score)
    {
        var llm = new FakeLlmClient();
        llm.Replies.Enqueue(new LlmReply($$"""{"score":{{score}}}""", LlmVerdict.Ok));
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

    [Fact]
    public async Task A_non_applicable_dimension_returns_null_without_calling_the_client()
    {
        var llm = ClientReturning(0.9);
        var result = await new Judge(llm, applies: false).ScoreAsync(Ctx, default);

        Assert.Null(result);        // omitted, not recorded
        Assert.Empty(llm.Calls);    // and no tokens spent — the judge never ran
    }
}
