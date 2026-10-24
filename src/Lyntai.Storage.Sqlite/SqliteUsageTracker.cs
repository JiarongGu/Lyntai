using System.Data;
using System.Data.Common;
using Dapper;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IUsageTracker"/> — spend/token accounting that survives restarts (so a usage
/// budget isn't reset to zero every deploy). One row per consumer, incremented in place; totals are SUMs
/// across rows. Fully async: <c>TotalAsync</c> is a PRE-CALL read on every budgeted request, so it must
/// not block a threadpool thread inside the async front door. Can reserve (<see cref="IReservingUsageTracker"/>):
/// holds live in <c>lyntai_usage_reservation</c>. Register with <c>UseSqliteUsageTracking()</c>.
/// </summary>
public sealed class SqliteUsageTracker(IDbConnectionFactory factory) : IReservingUsageTracker
{
    public async ValueTask RecordAsync(string consumer, ProviderUsage usage, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(UsageTrackerSql.Record,
            new { consumer = UsageTrackerSql.Consumer(consumer), @in = usage.InputTokens, @out = usage.OutputTokens, cost = usage.CostUsd ?? 0 },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async ValueTask<UsageTotals> TotalAsync(string? consumer = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // CAST(... AS REAL): a whole-number cost sum comes back with integer affinity otherwise.
        // COLLATE NOCASE still matches a row stored before UsageTrackerSql.Consumer folded the name.
        var row = consumer is null
            ? await conn.QuerySingleOrDefaultAsync<UsageTotalsRow>(new CommandDefinition("""
                SELECT COALESCE(SUM(input_tokens),0) AS input_tokens, COALESCE(SUM(output_tokens),0) AS output_tokens,
                       CAST(COALESCE(SUM(cost_usd),0) AS REAL) AS cost_usd, COALESCE(SUM(calls),0) AS calls
                FROM lyntai_usage
                """, cancellationToken: ct)).ConfigureAwait(false)
            : await conn.QuerySingleOrDefaultAsync<UsageTotalsRow>(new CommandDefinition("""
                SELECT COALESCE(SUM(input_tokens),0) AS input_tokens, COALESCE(SUM(output_tokens),0) AS output_tokens,
                       CAST(COALESCE(SUM(cost_usd),0) AS REAL) AS cost_usd, COALESCE(SUM(calls),0) AS calls
                FROM lyntai_usage WHERE consumer = @consumer COLLATE NOCASE
                """, new { consumer = UsageTrackerSql.Consumer(consumer) }, cancellationToken: ct)).ConfigureAwait(false);
        return row is null ? UsageTotals.Empty : new UsageTotals(row.InputTokens, row.OutputTokens, row.CostUsd, row.Calls);
    }

    public async ValueTask ResetAsync(string? consumer = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        if (consumer is null)
            await conn.ExecuteAsync(new CommandDefinition(UsageTrackerSql.ResetAll,
                cancellationToken: ct)).ConfigureAwait(false);
        else
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM lyntai_usage WHERE consumer = @consumer COLLATE NOCASE",
                new { consumer = UsageTrackerSql.Consumer(consumer) }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async ValueTask<UsageReservationResult> ReserveAsync(UsageReservationRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var consumer = UsageTrackerSql.Consumer(request.Consumer);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // Serializable is BEGIN IMMEDIATE here: the write lock is taken BEFORE the read, so no other writer can land
        // a hold between this check and this insert.
        await using var tx = await conn.BeginTransactionAsync(IsolationLevel.Serializable, ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(UsageTrackerSql.DropExpiredHolds,
            new { now = DateTimeOffset.UtcNow }, tx, cancellationToken: ct)).ConfigureAwait(false);

        var global = request.Caps.HasGlobal ? await LoadAsync(conn, tx, null, ct).ConfigureAwait(false) : UsageTotals.Empty;
        var mine = request.Caps.HasConsumer ? await LoadAsync(conn, tx, consumer, ct).ConfigureAwait(false) : UsageTotals.Empty;
        UsageReservationResult result;
        if (request.Caps.ExceededBy(global, mine, request.Amount) is { } cap)
            result = new UsageReservationResult(null, cap);
        else
        {
            var held = new UsageReservation(Guid.NewGuid().ToString("N"), request.Consumer, request.Amount, request.ExpiresAt);
            await conn.ExecuteAsync(new CommandDefinition(UsageTrackerSql.Hold,
                new { id = held.Id, consumer, @in = held.Amount.InputTokens, @out = held.Amount.OutputTokens,
                      cost = held.Amount.CostUsd ?? 0, expiresAt = held.ExpiresAt },
                tx, cancellationToken: ct)).ConfigureAwait(false);
            result = new UsageReservationResult(held);
        }

        await tx.CommitAsync(ct).ConfigureAwait(false); // a refusal still commits the expired-hold sweep
        return result;
    }

    public async ValueTask ReconcileAsync(UsageReservation reservation, ProviderUsage? actual, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(UsageTrackerSql.Release,
            new { id = reservation.Id }, tx, cancellationToken: ct)).ConfigureAwait(false);
        if (actual is not null)
            await conn.ExecuteAsync(new CommandDefinition(UsageTrackerSql.Record,
                new { consumer = UsageTrackerSql.Consumer(reservation.Consumer), @in = actual.InputTokens,
                      @out = actual.OutputTokens, cost = actual.CostUsd ?? 0 },
                tx, cancellationToken: ct)).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
    }

    /// <summary>Committed spend plus live holds, for one consumer or (null) everyone.</summary>
    private static async Task<UsageTotals> LoadAsync(DbConnection conn, DbTransaction tx, string? consumer, CancellationToken ct)
    {
        var where = consumer is null ? "" : "WHERE consumer = @consumer COLLATE NOCASE";
        var row = await conn.QuerySingleOrDefaultAsync<UsageTotalsRow>(new CommandDefinition($"""
            SELECT COALESCE(SUM(input_tokens),0) AS input_tokens, COALESCE(SUM(output_tokens),0) AS output_tokens,
                   CAST(COALESCE(SUM(cost_usd),0) AS REAL) AS cost_usd, 0 AS calls
            FROM (SELECT input_tokens, output_tokens, cost_usd FROM lyntai_usage {where}
                  UNION ALL
                  SELECT input_tokens, output_tokens, cost_usd FROM lyntai_usage_reservation {where}) AS load
            """, new { consumer }, tx, cancellationToken: ct)).ConfigureAwait(false);
        return row is null ? UsageTotals.Empty : new UsageTotals(row.InputTokens, row.OutputTokens, row.CostUsd, 0);
    }
}
