using Lyntai.Cortex;

namespace Lyntai.Storage;

/// <summary>Persisted scorer results, grouped by session.</summary>
public interface IScoreStore
{
    /// <summary>Persist a session's scores — an UPSERT on <c>(session_id, scorer_id)</c>: re-scoring a
    /// session REPLACES that scorer's row rather than accumulating a duplicate.</summary>
    Task SaveAsync(string sessionId, IReadOnlyList<ScoredResult> results, CancellationToken ct = default);

    Task<IReadOnlyList<ScoredResult>> GetAsync(string sessionId, CancellationToken ct = default);

    /// <summary>Cross-session per-scorer aggregate — mean score + count grouped by scorer, for the eval
    /// dashboard. Ordered by scorer id.</summary>
    Task<IReadOnlyList<ScorerAggregate>> AggregateAsync(CancellationToken ct = default);

    /// <summary>Bulk export of every stored <c>(session, scorer, score)</c> — a flat dump for a tuning
    /// dataset. Ordered by session then scorer.</summary>
    Task<IReadOnlyList<ScoreExportRow>> ExportAsync(CancellationToken ct = default);
}
