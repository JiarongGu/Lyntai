using System.Text.Json;
using Lyntai.Llm;
using Lyntai.Text;

namespace Lyntai.Cortex;

public enum PairwiseWinner { A, B, Tie }

/// <summary>Which of two candidate outputs a judge preferred for the same input.</summary>
public sealed record PairwiseResult(PairwiseWinner Winner, string? Reason = null);

/// <summary>An LLM judge that picks the better of two outputs for a given input — the pairwise-comparison
/// calibration mode (often more reliable than absolute 0..1 scoring).</summary>
public interface IPairwiseComparer
{
    Task<PairwiseResult> CompareAsync(string input, string outputA, string outputB, CancellationToken ct = default);
}

/// <summary>
/// Default comparer over <see cref="ILlmClient"/>. Position bias (LLM judges favor whichever answer
/// is shown first) is a documented failure mode, so by default this runs BOTH orders and only returns
/// a winner when the two passes agree on the same actual output — a disagreement is reported as a
/// <see cref="PairwiseWinner.Tie"/> (the judge isn't discriminating reliably). Set
/// <paramref name="mitigatePositionBias"/> false for a cheaper single pass.
/// </summary>
public sealed class LlmPairwiseComparer(ILlmClient llm, bool mitigatePositionBias = true) : IPairwiseComparer
{
    public async Task<PairwiseResult> CompareAsync(string input, string outputA, string outputB, CancellationToken ct = default)
    {
        if (!mitigatePositionBias)
            return await JudgeAsync(input, outputA, outputB, ct).ConfigureAwait(false);

        // the forward and position-swapped passes are independent judge calls — run them concurrently
        var forwardTask = JudgeAsync(input, outputA, outputB, ct);
        var swappedTask = JudgeAsync(input, outputB, outputA, ct);
        var first = await forwardTask.ConfigureAwait(false);
        var swapped = await swappedTask.ConfigureAwait(false);

        // a's-vs-b's identities are tracked, not the slot
        var secondForA = swapped.Winner switch
        {
            PairwiseWinner.A => PairwiseWinner.B, // "slot A" in the swapped call was outputB
            PairwiseWinner.B => PairwiseWinner.A,
            _ => PairwiseWinner.Tie,
        };

        if (first.Winner == secondForA)
            return first; // both passes agree on the same real output — trust it
        return new PairwiseResult(PairwiseWinner.Tie,
            $"position-bias check disagreed (forward: {first.Winner}, swapped: {secondForA})");
    }

    private async Task<PairwiseResult> JudgeAsync(string input, string a, string b, CancellationToken ct)
    {
        var req = new LlmRequest
        {
            Messages =
            [
                LlmMessage.System(
                    "You are a strict evaluator performing a SCORING TASK: pick which reply better answers " +
                    "the request. Reply with exactly one JSON object " +
                    """{"winner": "a" | "b" | "tie", "reason": "<short reason>"} and nothing else."""),
                LlmMessage.User($"[request]\n{input}\n\n[reply a]\n{a}\n\n[reply b]\n{b}"),
            ],
            JsonSchema = """{"type":"object","properties":{"winner":{"type":"string","enum":["a","b","tie"]},"reason":{"type":"string"}},"required":["winner"]}""",
            Consumer = "scoring",
        };

        var reply = await llm.CompleteJsonAsync(req, ct).ConfigureAwait(false);
        if (reply.Verdict != LlmVerdict.Ok || !TryParse(reply.Text, out var result))
            return new PairwiseResult(PairwiseWinner.Tie, "judge produced no usable verdict");
        return result;
    }

    internal static bool TryParse(string text, out PairwiseResult result)
    {
        result = new PairwiseResult(PairwiseWinner.Tie);
        if (!JsonExtract.TryParseObject(text, out var doc)) return false;
        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("winner", out var w) || w.ValueKind != JsonValueKind.String)
                return false;
            var winner = w.GetString()?.Trim().ToLowerInvariant() switch
            {
                "a" => PairwiseWinner.A,
                "b" => PairwiseWinner.B,
                _ => PairwiseWinner.Tie,
            };
            var reason = doc.RootElement.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String
                ? r.GetString()
                : null;
            result = new PairwiseResult(winner, reason);
            return true;
        }
    }
}
