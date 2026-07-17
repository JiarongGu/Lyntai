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
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO score_result (session_id, scorer_id, scorer_name, score_group, is_llm, score, reason, created_at)
                VALUES (@sessionId, @ScorerId, @ScorerName, @Group, @IsLlm, @Score, @Reason, @now)
                """, new { sessionId, r.ScorerId, r.ScorerName, r.Group, r.IsLlm, r.Score, r.Reason, now },
                tx, cancellationToken: ct)).ConfigureAwait(false);
        }
        tx.Commit();
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
            FROM score_result WHERE session_id = @sessionId ORDER BY id
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
}
