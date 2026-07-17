namespace Lyntai.Cortex;

/// <summary>Runs every registered <see cref="IScorer"/> over a context and aggregates the results
/// (persisting them when a score store is wired). Fail-open: a scorer that returns null or throws
/// is skipped, never sinks the evaluation.</summary>
public interface IScoringService
{
    Task<IReadOnlyList<ScoredResult>> EvaluateAsync(ScoreContext ctx, CancellationToken ct = default);
}
