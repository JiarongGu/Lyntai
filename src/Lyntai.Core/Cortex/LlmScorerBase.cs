using System.Text.Json;
using Lyntai.Llm;
using Lyntai.Text;

namespace Lyntai.Cortex;

/// <summary>
/// Base for LLM-judge scorers: one-shot call through the front door (the configured default
/// candidates), expecting a <c>{"score": 0..1, "reason": "…"}</c> verdict. Extraction + the
/// one-retry-on-parse-failure contract come from <see cref="LlmStructuredExtensions.CompleteJsonAsync"/>;
/// anything unusable → null (the dimension is skipped, never sinks the evaluation).
/// </summary>
public abstract class LlmScorerBase(ILlmClient llm) : IScorer
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public virtual string Group => "llm";
    public bool IsLlm => true;

    /// <summary>The model this judge runs on — null (default) uses the routed default for
    /// <see cref="Consumer"/>. Override to route a cheap judge to a cheap model (e.g. haiku) per scorer.</summary>
    protected virtual string? Model => null;

    /// <summary>The consumer tag for this judge's calls — drives per-consumer model + timeout routing.
    /// Default <c>"scoring"</c>; override per scorer to route different judges differently.</summary>
    protected virtual string Consumer => "scoring";

    /// <summary>Whether this dimension applies to <paramref name="ctx"/> — checked BEFORE the judge call, so
    /// a conditional scorer (e.g. a "faithfulness" dimension that applies to a plan but not a code-edit turn)
    /// skips WITHOUT spending tokens. Return false → <see cref="ScoreAsync"/> returns null (the dimension is
    /// omitted, never recorded). Default: always applies.</summary>
    protected virtual bool Applies(ScoreContext ctx) => true;

    /// <summary>Build the judge prompt for a context. The base wraps it with the verdict-format
    /// instruction; implementations only describe what to judge.</summary>
    protected abstract string BuildJudgePrompt(ScoreContext ctx);

    /// <summary>The judge's SYSTEM preamble. Virtual so a scorer can override the rubric/tone/language
    /// (the default is English + a strict-evaluator framing) — it MUST still instruct the model to reply
    /// with exactly the <c>{"score": &lt;0..1&gt;, "reason": "…"}</c> JSON object that
    /// <see cref="TryParseVerdict"/> expects.</summary>
    protected virtual string JudgeSystemPrompt =>
        "You are a strict evaluator performing a SCORING TASK. Reply with exactly one JSON object " +
        """{"score": <number 0..1>, "reason": "<short reason>"} and nothing else.""";

    public async Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct)
    {
        if (!Applies(ctx)) return null; // not applicable to this context — don't spend tokens on the judge

        var req = new LlmRequest
        {
            Messages =
            [
                LlmMessage.System(JudgeSystemPrompt),
                LlmMessage.User(BuildJudgePrompt(ctx)),
            ],
            JsonSchema = """{"type":"object","properties":{"score":{"type":"number"},"reason":{"type":"string"}},"required":["score"]}""",
            Model = Model,
            Consumer = Consumer,
        };

        var reply = await llm.CompleteJsonAsync(req, ct).ConfigureAwait(false);
        if (reply.Verdict != LlmVerdict.Ok) return null;
        return TryParseVerdict(reply.Text, out var result) ? result : null;
    }

    internal static bool TryParseVerdict(string text, out ScoreResult result)
    {
        result = new ScoreResult(0);
        if (!JsonExtract.TryParseObject(text, out var doc)) return false;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("score", out var scoreEl) || scoreEl.ValueKind != JsonValueKind.Number)
                return false;
            var score = Math.Clamp(scoreEl.GetDouble(), 0.0, 1.0);
            var reason = doc.RootElement.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                ? reasonEl.GetString()
                : null;
            result = new ScoreResult(score, reason);
            return true;
        }
    }
}
