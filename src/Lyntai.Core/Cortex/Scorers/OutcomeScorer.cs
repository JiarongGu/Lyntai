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
    public string Id => "outcome";
    public string Name => "Outcome";
    public string Group => "deterministic";
    public bool IsLlm => false;

    public Task<ScoreResult?> ScoreAsync(ScoreContext ctx, CancellationToken ct)
    {
        ScoreResult result;
        if (string.IsNullOrWhiteSpace(ctx.Output))
            result = new ScoreResult(0.0, "no output produced");
        else if (ctx.Extra is not null && ctx.Extra.ContainsKey("error"))
            result = new ScoreResult(0.2, $"output produced but an error was recorded: {ctx.Extra["error"]}");
        else
            result = new ScoreResult(1.0, "output produced without recorded errors");
        return Task.FromResult<ScoreResult?>(result);
    }
}
