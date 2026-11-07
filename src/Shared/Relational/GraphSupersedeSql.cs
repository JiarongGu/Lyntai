using System.Data;
using Dapper;
using Lyntai.Memory;
using Lyntai.Storage;

namespace Lyntai.Storage.Relational;

/// <summary>A supersession applied as ONE transaction on a relational store, once for both dialects (<c>docs/DECISIONS.md</c> D242):
/// the successor's upsert, each predecessor's end and the key's receipt commit together or not at all.
/// <para>Each predecessor is judged and then ended in turn, in the order <see cref="IEndableMemoryGraphStore.EndAsync"/> judges one —
/// missing, changed, a different live successor — plus a successor that would start before the predecessor began and the successor
/// itself. They are ALL judged even after one fails, so the result says which moved; a failure rolls the transaction back, which takes
/// the successor, the position it advanced and the ends already made with it. Each end is <see cref="MemoryGraphSql.End"/> itself, so a
/// rewrite landing between the judgement and the end still matches no row.</para></summary>
internal static class GraphSupersedeSql
{
    private const string State = """
        SELECT revision AS Revision, ended_reason AS EndedReason, superseded_by AS SupersededBy, valid_from AS ValidFrom,
               CASE WHEN EXISTS (SELECT 1 FROM lyntai_memory_node s WHERE s.id = lyntai_memory_node.superseded_by)
                    THEN 1 ELSE 0 END AS SuccessorLives
        FROM lyntai_memory_node WHERE id = @id AND engine = @engine
        """;

    private sealed class StateRow
    {
        public long Revision { get; set; }
        public string? EndedReason { get; set; }
        public long? SupersededBy { get; set; }
        public DateTimeOffset? ValidFrom { get; set; }
        public int SuccessorLives { get; set; }
    }

    private static Task<StateRow?> StateOfAsync(IDbConnection conn, IDbTransaction? tx, string engine, long id, CancellationToken ct) =>
        conn.QuerySingleOrDefaultAsync<StateRow>(new CommandDefinition(State, new { id, engine }, transaction: tx, cancellationToken: ct));

    /// <summary>Store the successor and end every predecessor, or change nothing. <paramref name="upsert"/> is the dialect's own
    /// upsert on the transaction this opens, returning the successor's id and revision.</summary>
    public static async Task<GraphSupersedeResult> RunAsync(IDbConnectionFactory factory, GraphNodeWrite successor,
        IReadOnlyList<GraphPredecessor> predecessors, string reason, DateTimeOffset at, GraphOperationKey? key,
        Func<IDbConnection, IDbTransaction, Task<(long Id, long Revision)>> upsert, CancellationToken ct)
    {
        var engine = successor.Engine;
        if (predecessors.Count == 0 || predecessors.Select(p => p.Id).Distinct().Count() != predecessors.Count)
            throw new ArgumentException("A supersession names at least one predecessor, and each only once.", nameof(predecessors));
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (key is not null)
        {
            await GraphReceiptSql.PurgeAsync(conn, tx, engine, key.At, ct).ConfigureAwait(false);
            if (await GraphReceiptSql.ReplayAsync(conn, tx, engine, key, ct).ConfigureAwait(false) is { } held)
            {
                await tx.CommitAsync(ct).ConfigureAwait(false);
                return await ReplayedAsync(conn, engine, held, predecessors, ct).ConfigureAwait(false);
            }
        }

        var (id, revision) = await upsert(conn, tx).ConfigureAwait(false);
        var start = (await StateOfAsync(conn, tx, engine, id, ct).ConfigureAwait(false))?.ValidFrom ?? at;
        var results = new List<GraphPredecessorResult>(predecessors.Count);
        foreach (var p in predecessors)
            results.Add(await EndOneAsync(conn, tx, engine, id, p, reason, start, ct).ConfigureAwait(false));

        if (results.Any(r => r.Standing != GraphPredecessorStanding.Ended))
        {
            await tx.RollbackAsync(ct).ConfigureAwait(false);
            // a racer that read the predecessor after the winner ended it finds the winner's receipt, now committed
            if (key is not null && await GraphReceiptSql.ReplayAsync(conn, null, engine, key, ct).ConfigureAwait(false) is { } won)
                return await ReplayedAsync(conn, engine, won, predecessors, ct).ConfigureAwait(false);
            // an end that landed inside the rolled-back transaction reports the revision it was judged at, which is the stored one
            return new GraphSupersedeResult(false, null, null,
                [.. results.Select((r, i) => r.Standing == GraphPredecessorStanding.Ended
                    ? r with { Standing = GraphPredecessorStanding.Unmoved, Revision = predecessors[i].ExpectedRevision } : r)],
                false, null);
        }

        GraphReceipt? receipt = null;
        if (key is not null)
        {
            receipt = await GraphReceiptSql.WriteAsync(conn, tx, engine, key, id, revision, MemoryOperationOutcome.Applied, ct).ConfigureAwait(false);
            if (receipt is null)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                var winner = await GraphReceiptSql.ReplayAsync(conn, null, engine, key, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"The operation key '{key.Key}' was written by another call and expired before it could be read; repeat the call.");
                return await ReplayedAsync(conn, engine, winner, predecessors, ct).ConfigureAwait(false);
            }
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new GraphSupersedeResult(true, id, revision, results, false, receipt);
    }

    private static async Task<GraphPredecessorResult> EndOneAsync(IDbConnection conn, IDbTransaction tx, string engine, long successor,
        GraphPredecessor p, string reason, DateTimeOffset start, CancellationToken ct)
    {
        var state = await StateOfAsync(conn, tx, engine, p.Id, ct).ConfigureAwait(false);
        if (state is null) return new GraphPredecessorResult(p.Id, GraphPredecessorStanding.Missing, null);
        // an entry is never its own successor, whatever revision the successor's refresh moved it to
        if (p.Id == successor) return new GraphPredecessorResult(p.Id, GraphPredecessorStanding.Refused, state.Revision);
        if (p.ExpectedRevision != state.Revision || state.EndedReason is not null)
            return new GraphPredecessorResult(p.Id, GraphPredecessorStanding.Changed, state.Revision);
        if ((state.SupersededBy is { } other && other != successor && state.SuccessorLives == 1)
            || (state.ValidFrom is { } began && start < began))
            return new GraphPredecessorResult(p.Id, GraphPredecessorStanding.Refused, state.Revision);

        var landed = await conn.ExecuteScalarAsync<long?>(new CommandDefinition(MemoryGraphSql.End, new
        {
            id = p.Id, engine, reason, by = (long?)successor, at = start.ToUniversalTime(), expected = (long?)p.ExpectedRevision,
        }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (landed is { } revision) return new GraphPredecessorResult(p.Id, GraphPredecessorStanding.Ended, revision);

        // a rewrite landed between the judgement and the end: say what it is now
        var result = MemoryGraphSql.Classify(await conn.QuerySingleOrDefaultAsync<EndStateRow>(new CommandDefinition(MemoryGraphSql.EndState,
            new { id = p.Id, engine }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false), p.ExpectedRevision, successor);
        return new GraphPredecessorResult(p.Id, result.Outcome switch
        {
            GraphEndOutcome.Refused => GraphPredecessorStanding.Refused,
            GraphEndOutcome.Missing => GraphPredecessorStanding.Missing,
            _ => GraphPredecessorStanding.Changed,
        }, result.Revision);
    }

    // a repeat the receipt answers: applied, naming the successor, with each predecessor as it stands now
    private static async Task<GraphSupersedeResult> ReplayedAsync(IDbConnection conn, string engine, GraphReceipt held,
        IReadOnlyList<GraphPredecessor> predecessors, CancellationToken ct)
    {
        var results = new List<GraphPredecessorResult>(predecessors.Count);
        foreach (var p in predecessors)
        {
            var state = await StateOfAsync(conn, null, engine, p.Id, ct).ConfigureAwait(false);
            results.Add(state is null
                ? new GraphPredecessorResult(p.Id, GraphPredecessorStanding.Missing, null)
                : new GraphPredecessorResult(p.Id, state.EndedReason is not null && state.SupersededBy == held.NodeId
                    ? GraphPredecessorStanding.Ended : GraphPredecessorStanding.Changed, state.Revision));
        }
        return new GraphSupersedeResult(true, held.NodeId, held.Revision, results, true, held);
    }
}
