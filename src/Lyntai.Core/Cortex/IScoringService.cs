namespace Lyntai.Cortex;

/// <summary>Runs every registered <see cref="IScorer"/> over a context and aggregates the results
/// (persisting them when a score store is wired). Fail-open: a scorer that returns null or throws
/// is skipped, never sinks the evaluation.</summary>
public interface IScoringService
{
    /// <summary>Score the context and persist when a store is wired.</summary>
    Task<IReadOnlyList<ScoredResult>> EvaluateAsync(ScoreContext ctx, CancellationToken ct = default);

    /// <summary>Score the context, persisting only when <paramref name="persist"/> is true — pass false for
    /// a dry/preview run (e.g. tuning a prompt) that must NOT write rows even when a score store is wired.</summary>
    Task<IReadOnlyList<ScoredResult>> EvaluateAsync(ScoreContext ctx, bool persist, CancellationToken ct = default);
}
