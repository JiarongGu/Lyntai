using Dapper;
using Lyntai.Cortex;

namespace Lyntai.Storage.Postgres;

public sealed class PostgresTraceStore(IDbConnectionFactory factory) : ITraceStore
{
    public async Task SaveAsync(RunTrace trace, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        // ITraceStore: a re-save REPLACES; the steps cascade on this delete.
        await conn.ExecuteAsync(new CommandDefinition(TraceStoreSql.DeleteTrace,
            new { trace.SessionId }, tx, cancellationToken: ct)).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(TraceStoreSql.InsertTrace,
            new { trace.SessionId, trace.Mode, trace.StartedAt, trace.EndedAt, trace.TraceId },
            tx, cancellationToken: ct)).ConfigureAwait(false);
        foreach (var s in TraceOrdinals.Stored(trace.Steps))
            await conn.ExecuteAsync(new CommandDefinition(TraceStoreSql.InsertStep,
                new { trace.SessionId, seq = s.Sequence, s.OffsetMs, s.Kind, s.Label, s.InputTokens, s.OutputTokens, s.CostUsd, s.DurationMs, s.Detail },
                tx, cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    public async Task<RunTrace?> GetAsync(string sessionId, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var header = await conn.QuerySingleOrDefaultAsync<TraceSessionRow>(new CommandDefinition(
            TraceStoreSql.GetTrace, new { sessionId }, cancellationToken: ct)).ConfigureAwait(false);
        if (header is null) return null;

        var steps = await conn.QueryAsync<TraceStepRow>(new CommandDefinition(
            TraceStoreSql.GetSteps, new { sessionId }, cancellationToken: ct)).ConfigureAwait(false);
        return header.ToRecord(steps);
    }
}
