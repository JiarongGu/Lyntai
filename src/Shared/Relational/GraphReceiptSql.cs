using System.Data;
using Dapper;
using Lyntai.Memory;

namespace Lyntai.Storage.Relational;

/// <summary>The receipt half of a keyed memory-graph call, once for both relational stores (<c>docs/DECISIONS.md</c> D242): every
/// statement is portable, so the twins differ only in how they open a transaction, and a keyed call reads in the same order on
/// both — purge, replay, effect, receipt.
/// <para><b>The purge is the FIRST statement, and that is load-bearing on SQLite</b>: a <c>DELETE</c> takes the write lock even
/// when it matches nothing, so two racing calls with one key serialize here and the loser's replay read already sees the winner's
/// receipt. A read first would give the loser a stale snapshot and a failed upgrade to a write.</para>
/// <para><b>Postgres serializes at the receipt's insert instead</b>: the loser waits on the primary key, finds
/// <c>DO NOTHING</c> affected no row, rolls its effect back and answers the winner's receipt.</para></summary>
internal static class GraphReceiptSql
{
    private const string Purge = "DELETE FROM lyntai_memory_receipt WHERE engine = @engine AND expires_at <= @now";

    private const string ByKey = """
        SELECT kind AS Kind, fingerprint AS Fingerprint, node_id AS NodeId, revision AS Revision, outcome AS Outcome,
               created_at AS CreatedAt, expires_at AS ExpiresAt
        FROM lyntai_memory_receipt
        WHERE engine = @engine AND task_key = @taskKey AND op_key = @key AND expires_at > @now
        """;

    private const string Insert = """
        INSERT INTO lyntai_memory_receipt
            (engine, task_key, op_key, kind, fingerprint, node_id, revision, outcome, created_at, expires_at)
        VALUES (@engine, @taskKey, @key, @kind, @fingerprint, @nodeId, @revision, @outcome, @at, @expiresAt)
        ON CONFLICT (engine, task_key, op_key) DO NOTHING
        """;

    private sealed class Row
    {
        public int Kind { get; set; }
        public string Fingerprint { get; set; } = "";
        public long NodeId { get; set; }
        public long Revision { get; set; }
        public int Outcome { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
    }

    /// <summary>What a keyed effect left that a receipt records.</summary>
    public readonly record struct Effect(long NodeId, long Revision, MemoryOperationOutcome Outcome);

    /// <summary>One keyed call: purge, replay, <paramref name="effect"/>, receipt — in one transaction. An effect that reports none
    /// (a stale revision, an entry already ended) leaves no receipt. A repeat answers <paramref name="replay"/> built from the
    /// receipt; a racing winner found at the receipt's insert rolls this call's effect back and does the same.</summary>
    public static async Task<GraphKeyed<T>> RunAsync<T>(IDbConnectionFactory factory, string engine, GraphOperationKey key,
        Func<GraphReceipt, T> replay, Func<IDbConnection, IDbTransaction, Task<(T Value, Effect? Effect)>> effect, CancellationToken ct)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await PurgeAsync(conn, tx, engine, key.At, ct).ConfigureAwait(false);
        if (await ReplayAsync(conn, tx, engine, key, ct).ConfigureAwait(false) is { } held)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new GraphKeyed<T>(replay(held), true, held);
        }

        var (value, landed) = await effect(conn, tx).ConfigureAwait(false);
        // an effect guarded by a revision that reports none may have lost a race: under READ COMMITTED it waited on the winner's row,
        // found it changed, and the winner's receipt is committed by now
        if (landed is null && await ReplayAsync(conn, tx, engine, key, ct).ConfigureAwait(false) is { } won)
        {
            await tx.CommitAsync(ct).ConfigureAwait(false);
            return new GraphKeyed<T>(replay(won), true, won);
        }
        GraphReceipt? receipt = null;
        if (landed is { } l)
        {
            receipt = await WriteAsync(conn, tx, engine, key, l.NodeId, l.Revision, l.Outcome, ct).ConfigureAwait(false);
            if (receipt is null)
            {
                await tx.RollbackAsync(ct).ConfigureAwait(false);
                var winner = await ReplayAsync(conn, null, engine, key, ct).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"The operation key '{key.Key}' was written by another call and expired before it could be read; repeat the call.");
                return new GraphKeyed<T>(replay(winner), true, winner);
            }
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new GraphKeyed<T>(value, false, receipt);
    }

    /// <summary>Remove the engine's receipts that expired at <paramref name="now"/>.</summary>
    public static Task PurgeAsync(IDbConnection conn, IDbTransaction tx, string engine, DateTimeOffset now, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(Purge, new { engine, now = now.ToUniversalTime() }, transaction: tx, cancellationToken: ct));

    /// <summary>The key's live receipt, or null.</summary>
    public static async Task<GraphReceipt?> ReadAsync(IDbConnection conn, IDbTransaction? tx, string engine, string taskKey, string key,
        DateTimeOffset now, CancellationToken ct)
    {
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(ByKey,
            new { engine, taskKey, key, now = now.ToUniversalTime() }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        return row is null
            ? null
            : new GraphReceipt(key, taskKey, (MemoryOperationKind)row.Kind, row.Fingerprint, row.NodeId, row.Revision,
                (MemoryOperationOutcome)row.Outcome, row.CreatedAt, row.ExpiresAt);
    }

    /// <summary>The receipt that already answers <paramref name="key"/>, or null. One that answered a different call refuses.</summary>
    /// <exception cref="MemoryKeyConflictException">The key answered another kind or other content.</exception>
    public static async Task<GraphReceipt?> ReplayAsync(IDbConnection conn, IDbTransaction? tx, string engine, GraphOperationKey key,
        CancellationToken ct)
    {
        if (await ReadAsync(conn, tx, engine, key.TaskKey, key.Key, key.At, ct).ConfigureAwait(false) is not { } held) return null;
        if (held.Kind != key.Kind || !string.Equals(held.Fingerprint, key.Fingerprint, StringComparison.Ordinal))
            throw new MemoryKeyConflictException(key.Key, key.TaskKey, held.Kind);
        return held;
    }

    /// <summary>Write the key's receipt; null when another transaction wrote it first, so the caller rolls back and answers that one.</summary>
    public static async Task<GraphReceipt?> WriteAsync(IDbConnection conn, IDbTransaction tx, string engine, GraphOperationKey key,
        long nodeId, long revision, MemoryOperationOutcome outcome, CancellationToken ct)
    {
        var written = await conn.ExecuteAsync(new CommandDefinition(Insert, new
        {
            engine, taskKey = key.TaskKey, key = key.Key, kind = (int)key.Kind, fingerprint = key.Fingerprint, nodeId, revision,
            outcome = (int)outcome, at = key.At.ToUniversalTime(), expiresAt = key.ExpiresAt.ToUniversalTime(),
        }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        return written == 0
            ? null
            : new GraphReceipt(key.Key, key.TaskKey, key.Kind, key.Fingerprint, nodeId, revision, outcome, key.At.ToUniversalTime(),
                key.ExpiresAt.ToUniversalTime());
    }
}
