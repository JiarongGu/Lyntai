using Lyntai.Lifecycle;
using System.Text.Json;
using Lyntai.Llm;
using Lyntai.Text;

namespace Lyntai.Cortex;

public enum PairwiseWinner { A, B, Tie }

/// <summary>Which of two candidate outputs a judge preferred for the same input.</summary>
/// <param name="Winner">The preferred output, or <see cref="PairwiseWinner.Tie"/>.</param>
/// <param name="Reason">The judge's own words, when it gave any.</param>
public sealed record PairwiseResult(PairwiseWinner Winner, string? Reason = null)
{
    /// <summary>Whether the model actually produced a usable answer. <b><see cref="PairwiseWinner.Tie"/>
    /// with this FALSE is not a verdict of "neither is better"</b> — it is "no verdict", which a judge
    /// outage, a refusal and an unparseable reply all look like. Collapsing the two lets a model being down
    /// read as a substantive result, which is the same conflation
    /// <see cref="Lyntai.Memory.Verification.MemoryVerification.Judged"/> exists to prevent one subsystem
    /// over.
    ///
    /// <para><b>It does not mean the answer was decisive.</b> A judge that answered "tie", and a
    /// two-pass run whose passes disagreed, are both true — the model spoke in both. Only the absence of a
    /// usable answer is false.</para>
    ///
    /// <para>Defaults to TRUE so a comparer written before this existed keeps meaning what it meant. It is
    /// a property rather than a positional parameter because adding one would change this record's
    /// constructor and <c>Deconstruct</c>, which the frozen surface (<b>D70</b>) does not allow.</para>
    /// </summary>
    public bool Judged { get; init; } = true;

    /// <summary>No usable answer from the judge. <see cref="PairwiseWinner.Tie"/> is carried as the safe
    /// neutral — a caller that ignores <see cref="Judged"/> behaves exactly as it did before this
    /// existed — and <paramref name="reason"/> says which failure it was.</summary>
    public static PairwiseResult NoOpinion(string reason) =>
        new(PairwiseWinner.Tie, reason) { Judged = false };
}

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
    /// <summary>The verdict CODE can reach, so the judge is not asked for it.
    ///
    /// <para><b>It is a correctness fix before it is a saving.</b> On identical text there is no signal for
    /// a judge to overcome its position bias with, so it can answer "a" — a false verdict the two-pass
    /// check cannot catch, because both passes see the same two strings. Code answers it certainly and for
    /// free, which is the standing rule that a model is not better at exact comparison
    /// (<c>.claude/knowledge/model-decoupling.md</c>).</para>
    ///
    /// <para><b>ORDINAL equality and deliberately nothing looser.</b> Whether trailing whitespace or casing
    /// matters is a judgement about the caller's domain — a formatting eval would say it does — so the
    /// model is still asked about anything short of identical.</para></summary>
    private static readonly PairwiseResult Identical =
        new(PairwiseWinner.Tie, "outputs are identical — no judge was asked");

    public async Task<PairwiseResult> CompareAsync(string input, string outputA, string outputB, CancellationToken ct = default)
    {
        // Judged stays TRUE: "neither is better" is the CORRECT answer here, not an absent one, and
        // reporting it as no-opinion would make a certainty read like a judge outage.
        if (string.Equals(outputA, outputB, StringComparison.Ordinal)) return Identical;

        if (!mitigatePositionBias)
            return await JudgeAsync(input, outputA, outputB, ct).ConfigureAwait(false);

        // the forward and position-swapped passes are independent judge calls — run them concurrently,
        // but observe BOTH before reading either: if the first await threw (cancel/transport), the other
        // pass would be abandoned unobserved and keep spending judge tokens with nothing to reap it
        var forwardTask = JudgeAsync(input, outputA, outputB, ct);
        var swappedTask = JudgeAsync(input, outputB, outputA, ct);
        await Task.WhenAll(forwardTask, swappedTask).ConfigureAwait(false);
        var first = forwardTask.Result;
        var swapped = swappedTask.Result;

        // a's-vs-b's identities are tracked, not the slot
        var secondForA = swapped.Winner switch
        {
            PairwiseWinner.A => PairwiseWinner.B, // "slot A" in the swapped call was outputB
            PairwiseWinner.B => PairwiseWinner.A,
            _ => PairwiseWinner.Tie,
        };

        // A pass that produced no usable answer poisons the pair: there were never two verdicts to compare,
        // so agreement between them would be agreement with a failure.
        if (!first.Judged || !swapped.Judged)
            return PairwiseResult.NoOpinion(
                $"a position-bias pass produced no usable verdict (forward judged: {first.Judged}, " +
                $"swapped judged: {swapped.Judged})");

        if (first.Winner == secondForA)
            return first; // both passes agree on the same real output — trust it

        // The model DID answer, twice, and contradicted itself — a real judgement that it is not
        // discriminating on this pair, not an absence of one. So this stays Judged.
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
            Consumer = LlmConsumers.Scoring,
        };

        var reply = await llm.CompleteJsonAsync(req, ct).ConfigureAwait(false);
        if (reply.Verdict != ProviderVerdict.Ok || !TryParse(reply.Text, out var result))
            return PairwiseResult.NoOpinion("judge produced no usable verdict");
        return result;
    }

    internal static bool TryParse(string text, out PairwiseResult result)
    {
        // the out-value on the FALSE path is not judged: it is only meant to be read when this returns
        // true, and a caller that ignores the bool must not receive a fabricated verdict
        result = PairwiseResult.NoOpinion("unparseable judge reply");
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
