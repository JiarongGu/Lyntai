namespace Lyntai.Storage;

/// <summary>The relational <see cref="ITraceStore"/> statements, every one shared by both backends. Steps are
/// inserted in the order <see cref="TraceOrdinals.Stored"/> returns, so <c>ORDER BY seq, id</c> reads them
/// back in it.</summary>
public static class TraceStoreSql
{
    public const string InsertTrace = """
        INSERT INTO lyntai_run_trace (session_id, mode, started_at, ended_at, trace_id)
        VALUES (@SessionId, @Mode, @StartedAt, @EndedAt, @TraceId)
        """;

    public const string InsertStep = """
        INSERT INTO lyntai_trace_step (session_id, seq, offset_ms, kind, label, input_tokens, output_tokens, cost_usd, duration_ms, detail)
        VALUES (@SessionId, @seq, @OffsetMs, @Kind, @Label, @InputTokens, @OutputTokens, @CostUsd, @DurationMs, @Detail)
        """;

    public const string GetTrace = """
        SELECT session_id AS SessionId, mode AS Mode, started_at AS StartedAt, ended_at AS EndedAt, trace_id AS TraceId
        FROM lyntai_run_trace WHERE session_id = @sessionId
        """;

    /// <summary>A session's steps in stored order. <c>CAST(… AS DOUBLE PRECISION)</c> is the portable spelling
    /// of SQLite's affinity guard: a whole-number cost stored as an INTEGER still reads back a double.</summary>
    public const string GetSteps = """
        SELECT seq AS Sequence, offset_ms AS OffsetMs, kind AS Kind, label AS Label,
               input_tokens AS InputTokens, output_tokens AS OutputTokens,
               CAST(cost_usd AS DOUBLE PRECISION) AS CostUsd, duration_ms AS DurationMs, detail AS Detail
        FROM lyntai_trace_step WHERE session_id = @sessionId ORDER BY seq, id
        """;

    /// <summary>Replacing a trace deletes the old one first; the steps follow by cascade.</summary>
    public const string DeleteTrace = "DELETE FROM lyntai_run_trace WHERE session_id = @SessionId";
}
