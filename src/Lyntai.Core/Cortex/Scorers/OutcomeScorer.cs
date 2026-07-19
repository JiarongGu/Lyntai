namespace Lyntai.Cortex.Scorers;

/// <summary>
/// Deterministic, domain-agnostic outcome check: did the exchange produce a usable result?
/// - empty/whitespace output → 0.0
/// - <c>Extra["error"]</c> set (the caller recorded a fault) → 0.2 (something came out, but broken)
/// - otherwise → 1.0
/// Register with <c>builder.AddScorer&lt;OutcomeScorer&gt;()</c>.
/// </summary>
public sealed class OutcomeScorer : IScorer
{
    /// <summary>The <see cref="ScoreContext.Extra"/> key this scorer reads to detect a recorded fault —
    /// exposed (not a bare literal) so a caller populating <c>Extra</c> uses the same key the scorer checks:
    /// <c>ctx.Extra[OutcomeScorer.ErrorKey] = "…"</c>.</summary>
    public const string ErrorKey = "error";

    public string Id => "outcome";
    public string Name => "Outcome";
    public string Group => "deterministic";
    public bool IsLlm => false;

    public Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct)
    {
        ScoreResult result;
        if (string.IsNullOrWhiteSpace(ctx.Output))
            result = new ScoreResult(0.0, "no output produced");
        else if (ctx.Extra is not null && ctx.Extra.ContainsKey(ErrorKey))
            result = new ScoreResult(0.2, $"output produced but an error was recorded: {ctx.Extra[ErrorKey]}");
        else
            result = new ScoreResult(1.0, "output produced without recorded errors");
        return Task.FromResult<ScoreResult?>(result);
    }
}
