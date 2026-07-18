using Dapper;
using Lyntai.Cortex;

namespace Lyntai.Storage.Sqlite;

public sealed class SqliteScoreStore(IDbConnectionFactory factory) : IScoreStore
{
    public async Task SaveAsync(string sessionId, IReadOnlyList<ScoredResult> results, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        using var tx = conn.BeginTransaction();
        var now = DateTimeOffset.UtcNow;
        foreach (var r in results)
        {
            // upsert on (session_id, scorer_id): re-scoring a session REPLACES that scorer's row
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO lyntai_score_result (session_id, scorer_id, scorer_name, score_group, is_llm, score, reason, created_at)
                VALUES (@sessionId, @ScorerId, @ScorerName, @Group, @IsLlm, @Score, @Reason, @now)
                ON CONFLICT(session_id, scorer_id) DO UPDATE SET
                    scorer_name=excluded.scorer_name, score_group=excluded.score_group, is_llm=excluded.is_llm,
                    score=excluded.score, reason=excluded.reason, created_at=excluded.created_at
                """, new { sessionId, r.ScorerId, r.ScorerName, r.Group, r.IsLlm, r.Score, r.Reason, now },
                tx, cancellationToken: ct)).ConfigureAwait(false);
        }
        tx.Commit();
    }

    public async Task<IReadOnlyList<ScorerAggregate>> AggregateAsync(CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<AggRow>(new CommandDefinition("""
            SELECT scorer_id AS ScorerId, MAX(scorer_name) AS ScorerName,
                   AVG(CAST(score AS REAL)) AS AverageScore, COUNT(*) AS Count
            FROM lyntai_score_result GROUP BY scorer_id ORDER BY scorer_id
            """, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => new ScorerAggregate(r.ScorerId, r.ScorerName, r.AverageScore, (int)r.Count))];
    }

    public async Task<IReadOnlyList<ScoreExportRow>> ExportAsync(CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<ExportRow>(new CommandDefinition("""
            SELECT session_id AS SessionId, scorer_id AS ScorerId, CAST(score AS REAL) AS Score
            FROM lyntai_score_result ORDER BY session_id, scorer_id
            """, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => new ScoreExportRow(r.SessionId, r.ScorerId, r.Score))];
    }

    public async Task<IReadOnlyList<ScoredResult>> GetAsync(string sessionId, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        // CAST(score AS REAL): the SQLite integer-affinity trap — a stored 1.0 must come back a double.
        // Mutable row shape: Dapper's record-constructor matching wants exact provider types (is_llm
        // arrives as long), property mapping converts flexibly.
        var rows = await conn.QueryAsync<ScoreRow>(new CommandDefinition("""
            SELECT scorer_id AS ScorerId, scorer_name AS ScorerName, score_group AS ScoreGroup,
                   is_llm AS IsLlm, CAST(score AS REAL) AS Score, reason AS Reason
            FROM lyntai_score_result WHERE session_id = @sessionId ORDER BY id
            """, new { sessionId }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => new ScoredResult(r.ScorerId, r.ScorerName, r.ScoreGroup, r.IsLlm, r.Score, r.Reason))];
    }

    private sealed class ScoreRow
    {
        public string ScorerId { get; set; } = "";
        public string ScorerName { get; set; } = "";
        public string ScoreGroup { get; set; } = "";
        public bool IsLlm { get; set; }
        public double Score { get; set; }
        public string? Reason { get; set; }
    }

    private sealed class AggRow
    {
        public string ScorerId { get; set; } = "";
        public string ScorerName { get; set; } = "";
        public double AverageScore { get; set; }
        public long Count { get; set; }
    }

    private sealed class ExportRow
    {
        public string SessionId { get; set; } = "";
        public string ScorerId { get; set; } = "";
        public double Score { get; set; }
    }
}
