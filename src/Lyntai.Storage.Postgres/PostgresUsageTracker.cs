using Dapper;
using Lyntai.Inference;
using Lyntai.Inference.Budgeting;

namespace Lyntai.Storage.Postgres;

/// <summary>
/// PostgreSQL-backed <see cref="IUsageTracker"/> — spend/token accounting that survives restarts and can be
/// shared across processes. One row per consumer, incremented in place; totals are SUMs across rows. Fully
/// async: <c>TotalAsync</c> is a PRE-CALL read on every budgeted request — a NETWORK round-trip here — so
/// it must not block a threadpool thread inside the async front door. Register with
/// <c>UsePostgresUsageTracking()</c>.
/// </summary>
public sealed class PostgresUsageTracker(IDbConnectionFactory factory) : IUsageTracker
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
        // lower() still matches a row stored before UsageTrackerSql.Consumer folded the name.
        var row = consumer is null
            ? await conn.QuerySingleOrDefaultAsync<UsageTotalsRow>(new CommandDefinition("""
                SELECT COALESCE(SUM(input_tokens),0)::bigint AS input_tokens,
                       COALESCE(SUM(output_tokens),0)::bigint AS output_tokens,
                       COALESCE(SUM(cost_usd),0)::double precision AS cost_usd,
                       COALESCE(SUM(calls),0)::bigint AS calls
                FROM lyntai_usage
                """, cancellationToken: ct)).ConfigureAwait(false)
            : await conn.QuerySingleOrDefaultAsync<UsageTotalsRow>(new CommandDefinition("""
                SELECT COALESCE(SUM(input_tokens),0)::bigint AS input_tokens,
                       COALESCE(SUM(output_tokens),0)::bigint AS output_tokens,
                       COALESCE(SUM(cost_usd),0)::double precision AS cost_usd,
                       COALESCE(SUM(calls),0)::bigint AS calls
                FROM lyntai_usage WHERE lower(consumer) = lower(@consumer)
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
                "DELETE FROM lyntai_usage WHERE lower(consumer) = lower(@consumer)",
                new { consumer = UsageTrackerSql.Consumer(consumer) }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
