using Lyntai.Text;

namespace Lyntai.Cortex.Scorers;

/// <summary>
/// Deterministic format check: is the output well-formed for its DECLARED format?
/// The caller declares the expectation via <c>Extra[FormatKey]</c> (see <see cref="FormatKey"/>):
/// - <c>"json"</c>: whole output parses → 1.0; a JSON object is extractable from prose/fences → 0.7;
///   nothing parseable → 0.0
/// - <c>"nonempty"</c>: non-whitespace output → 1.0 else 0.0
/// - no declared format → null (not applicable — this scorer never guesses)
/// Register with <c>builder.AddScorer&lt;StructureScorer&gt;()</c>.
/// </summary>
public sealed class StructureScorer : IScorer
{
    /// <summary>The <see cref="ScoreContext.Extra"/> key this scorer reads to learn the DECLARED format —
    /// exposed (not a bare literal) so a caller populating <c>Extra</c> uses the same key the scorer checks:
    /// <c>ctx.Extra[StructureScorer.FormatKey] = "json"</c>. The mirror of
    /// <see cref="OutcomeScorer.ErrorKey"/>; a key that only exists as a literal inside the scorer is a
    /// contract a caller can only get right by reading the source.</summary>
    public const string FormatKey = "format";

    public string Id => "structure";
    public string Name => "Structure";
    public string Group => "deterministic";
    public bool IsLlm => false;

    public Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct = default)
    {
        var format = ctx.Extra is not null && ctx.Extra.TryGetValue(FormatKey, out var f) ? f : null;
        return Task.FromResult(format switch
        {
            "json" => ScoreJson(ctx.Output),
            "nonempty" => new ScoreResult(string.IsNullOrWhiteSpace(ctx.Output) ? 0.0 : 1.0),
            _ => (ScoreResult?)null, // no declared format — not applicable
        });
    }

    private static ScoreResult ScoreJson(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return new ScoreResult(0.0, "empty output, json expected");
        if (JsonExtract.IsValid(output)) return new ScoreResult(1.0, "output is valid json");

        var embedded = JsonExtract.ExtractObject(output);
        if (JsonExtract.IsValid(embedded))
            return new ScoreResult(0.7, "json extractable from surrounding prose");
        return new ScoreResult(0.0, "no parseable json in output");
    }
}
