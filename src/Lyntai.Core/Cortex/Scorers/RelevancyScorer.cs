using Lyntai.Llm;

namespace Lyntai.Cortex.Scorers;

/// <summary>LLM-judge dimension: how relevant is the output to the input? One-shot call through the
/// front door, expecting the standard <c>{score,reason}</c> verdict — everything inherited from
/// <see cref="LlmScorerBase"/>. Register with <c>builder.AddScorer&lt;RelevancyScorer&gt;()</c>.</summary>
public sealed class RelevancyScorer(ILlmClient llm) : LlmScorerBase(llm)
{
    public override string Id => "relevancy";
    public override string Name => "Relevancy";

    protected override string BuildJudgePrompt(ScoreContext ctx) => $"""
        SCORING TASK: judge how relevant the reply is to the request, 0 (unrelated) to 1 (fully on-point).

        [request]
        {ctx.Input}

        [reply]
        {ctx.Output}
        """;
}
