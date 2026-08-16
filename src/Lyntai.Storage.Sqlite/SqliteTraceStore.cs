using Dapper;
using Lyntai.Cortex;

namespace Lyntai.Storage.Sqlite;

public sealed class SqliteTraceStore(IDbConnectionFactory factory) : ITraceStore
{
    public async Task SaveAsync(RunTrace trace, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        using var tx = conn.BeginTransaction();

        // saving a session again replaces its trace (steps cascade on delete)
        await conn.ExecuteAsync(new CommandDefinition(
            TraceStoreSql.DeleteTrace,
            new { trace.SessionId }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await conn.ExecuteAsync(new CommandDefinition(TraceStoreSql.InsertTrace, new { trace.SessionId, trace.Mode, trace.StartedAt, trace.EndedAt, trace.TraceId },
            tx, cancellationToken: ct)).ConfigureAwait(false);

        for (var i = 0; i < trace.Steps.Count; i++)
        {
            var s = trace.Steps[i];
            // seq is the step's timeline ordinal (Sequence when set by a recorder; the list position otherwise,
            // so a hand-built trace with unset Sequence still persists a monotonic, distinct order)
            var seq = s.Sequence != 0 ? s.Sequence : i;
            await conn.ExecuteAsync(new CommandDefinition(TraceStoreSql.InsertStep, new { trace.SessionId, seq, s.OffsetMs, s.Kind, s.Label, s.InputTokens, s.OutputTokens, s.CostUsd, s.DurationMs, s.Detail },
                tx, cancellationToken: ct)).ConfigureAwait(false);
        }
        tx.Commit();
    }

    public async Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);

        var header = await conn.QuerySingleOrDefaultAsync<TraceSessionRow>(new CommandDefinition(TraceStoreSql.GetTrace, new { sessionId }, cancellationToken: ct)).ConfigureAwait(false);
        if (header is null) return null;

        var steps = await conn.QueryAsync<TraceStepRow>(new CommandDefinition("""
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


}
