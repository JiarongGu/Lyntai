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

    /// <summary>Build the judge prompt for a context. The base wraps it with the verdict-format
    /// instruction; implementations only describe what to judge.</summary>
    protected abstract string BuildJudgePrompt(ScoreContext ctx);

    public async Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct)
    {
        var req = new LlmRequest
        {
            Messages =
            [
                LlmMessage.System(
                    "You are a strict evaluator performing a SCORING TASK. Reply with exactly one JSON object " +
                    """{"score": <number 0..1>, "reason": "<short reason>"} and nothing else."""),
                LlmMessage.User(BuildJudgePrompt(ctx)),
            ],
            JsonSchema = """{"type":"object","properties":{"score":{"type":"number"},"reason":{"type":"string"}},"required":["score"]}""",
            Consumer = "scoring",
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
