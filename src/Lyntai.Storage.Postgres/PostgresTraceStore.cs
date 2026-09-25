using Dapper;
using Lyntai.Cortex;

namespace Lyntai.Storage.Postgres;

public sealed class PostgresTraceStore(IDbConnectionFactory factory) : ITraceStore
{
    public async Task SaveAsync(RunTrace trace, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        using var tx = conn.BeginTransaction();

        // ITraceStore: a re-save REPLACES; the steps cascade on this delete.
        await conn.ExecuteAsync(new CommandDefinition(
            TraceStoreSql.DeleteTrace,
            new { trace.SessionId }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await conn.ExecuteAsync(new CommandDefinition(TraceStoreSql.InsertTrace, new { trace.SessionId, trace.Mode, trace.StartedAt, trace.EndedAt, trace.TraceId },
            tx, cancellationToken: ct)).ConfigureAwait(false);

        // inserted in STORED order, so the id tiebreak of the read's ORDER BY seq, id agrees with it
        foreach (var s in TraceOrdinals.Stored(trace.Steps))
            await conn.ExecuteAsync(new CommandDefinition(TraceStoreSql.InsertStep, new { trace.SessionId, seq = s.Sequence, s.OffsetMs, s.Kind, s.Label, s.InputTokens, s.OutputTokens, s.CostUsd, s.DurationMs, s.Detail },
                tx, cancellationToken: ct)).ConfigureAwait(false);
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
                   cost_usd AS CostUsd, duration_ms AS DurationMs, detail AS Detail
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
