using Dapper;
using Lyntai.Cortex;

namespace Lyntai.Storage.Sqlite;

public sealed class SqliteTraceStore(IDbConnectionFactory factory) : ITraceStore
{
    public async Task SaveAsync(RunTrace trace, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        using var tx = conn.BeginTransaction();

        // saving a session again replaces its trace (steps cascade on delete)
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_run_trace WHERE session_id = @SessionId",
            new { trace.SessionId }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_run_trace (session_id, mode, started_at, ended_at, trace_id)
            VALUES (@SessionId, @Mode, @StartedAt, @EndedAt, @TraceId)
            """, new { trace.SessionId, trace.Mode, trace.StartedAt, trace.EndedAt, trace.TraceId },
            tx, cancellationToken: ct)).ConfigureAwait(false);

        for (var i = 0; i < trace.Steps.Count; i++)
        {
            var s = trace.Steps[i];
            // seq is the step's timeline ordinal (Sequence when set by a recorder; the list position otherwise,
            // so a hand-built trace with unset Sequence still persists a monotonic, distinct order)
            var seq = s.Sequence != 0 ? s.Sequence : i;
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO lyntai_trace_step (session_id, seq, offset_ms, kind, label, input_tokens, output_tokens, cost_usd, duration_ms, detail)
                VALUES (@SessionId, @seq, @OffsetMs, @Kind, @Label, @InputTokens, @OutputTokens, @CostUsd, @DurationMs, @Detail)
                """, new { trace.SessionId, seq, s.OffsetMs, s.Kind, s.Label, s.InputTokens, s.OutputTokens, s.CostUsd, s.DurationMs, s.Detail },
                tx, cancellationToken: ct)).ConfigureAwait(false);
        }
        tx.Commit();
    }

    public async Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        using var conn = factory.Open();

        var header = await conn.QuerySingleOrDefaultAsync<TraceRow>(new CommandDefinition("""
            SELECT session_id AS SessionId, mode AS Mode, started_at AS StartedAt, ended_at AS EndedAt, trace_id AS TraceId
            FROM lyntai_run_trace WHERE session_id = @sessionId
            """, new { sessionId }, cancellationToken: ct)).ConfigureAwait(false);
        if (header is null) return null;

        var steps = await conn.QueryAsync<StepRow>(new CommandDefinition("""
            SELECT seq AS Sequence, offset_ms AS OffsetMs, kind AS Kind, label AS Label,
                   input_tokens AS InputTokens, output_tokens AS OutputTokens,
                   CAST(cost_usd AS REAL) AS CostUsd, duration_ms AS DurationMs, detail AS Detail
            FROM lyntai_trace_step WHERE session_id = @sessionId ORDER BY seq, id
            """, new { sessionId }, cancellationToken: ct)).ConfigureAwait(false);

        return new RunTrace
        {
            SessionId = header.SessionId,
            Mode = header.Mode,
            StartedAt = header.StartedAt,
            EndedAt = header.EndedAt,
            TraceId = header.TraceId,
            Steps = [.. steps.Select(s => new TraceStep
            {
                Kind = s.Kind,
                Label = s.Label,
                Sequence = s.Sequence,
                OffsetMs = s.OffsetMs,
                InputTokens = s.InputTokens,
                OutputTokens = s.OutputTokens,
                CostUsd = s.CostUsd,
                DurationMs = s.DurationMs,
                Detail = s.Detail,
            })],
        };
    }

    // mutable row shapes for Dapper; mapped to the Core records by hand
    private sealed class TraceRow
    {
        public string SessionId { get; set; } = "";
        public string Mode { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? EndedAt { get; set; }
        public string? TraceId { get; set; }
    }

    private sealed class StepRow
    {
        public string Kind { get; set; } = "";
        public string Label { get; set; } = "";
        public long Sequence { get; set; }
        public long OffsetMs { get; set; }
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public double CostUsd { get; set; }
        public long DurationMs { get; set; }
        public string? Detail { get; set; }
    }
}
