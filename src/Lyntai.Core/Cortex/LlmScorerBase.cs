using System.Text.Json;
using Lyntai.Llm;

namespace Lyntai.Cortex;

/// <summary>
/// Base for LLM-judge scorers: one-shot call through the router (from a neutral context, using the
/// configured default candidates), expecting a <c>{"score": 0..1, "reason": "…"}</c> verdict.
/// Tolerant JSON extraction + one retry on parse failure (design §6); anything else → null (skipped).
/// </summary>
public abstract class LlmScorerBase(ILlmRouter router, LyntaiOptions options) : IScorer
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
        if (options.DefaultCandidates.Count == 0) return null; // nowhere to route the judge call

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

        for (var attempt = 0; attempt < 2; attempt++) // one retry on parse failure
        {
            var reply = await router.CompleteAsync(options.DefaultCandidates, req, ct).ConfigureAwait(false);
            if (reply.Verdict != LlmVerdict.Ok) return null;

            if (TryParseVerdict(reply.Text, out var result)) return result;
        }
        return null;
    }

    internal static bool TryParseVerdict(string text, out ScoreResult result)
    {
        result = new ScoreResult(0);
        var json = JsonExtract.ExtractObject(text);
        if (json is null) return false;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("score", out var scoreEl) || scoreEl.ValueKind != JsonValueKind.Number)
                return false;
            var score = Math.Clamp(scoreEl.GetDouble(), 0.0, 1.0);
            var reason = doc.RootElement.TryGetProperty("reason", out var reasonEl) && reasonEl.ValueKind == JsonValueKind.String
                ? reasonEl.GetString()
                : null;
            result = new ScoreResult(score, reason);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
