using Dapper;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;

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
    public async ValueTask RecordAsync(string consumer, LlmUsage usage, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_usage (consumer, input_tokens, output_tokens, cost_usd, calls)
            VALUES (@consumer, @in, @out, @cost, 1)
            ON CONFLICT (consumer) DO UPDATE SET
                input_tokens  = lyntai_usage.input_tokens  + @in,
                output_tokens = lyntai_usage.output_tokens + @out,
                cost_usd      = lyntai_usage.cost_usd      + @cost,
                calls         = lyntai_usage.calls         + 1
            """, new { consumer, @in = usage.InputTokens, @out = usage.OutputTokens, cost = usage.CostUsd ?? 0 },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async ValueTask<UsageTotals> TotalAsync(string? consumer = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // Per-consumer: SUM + lower() — rows keep their exact casing (the TEXT PK), but consumer identity
        // is case-insensitive library-wide, so the total AGGREGATES across casings.
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
                """, new { consumer }, cancellationToken: ct)).ConfigureAwait(false);
        return row is null ? UsageTotals.Empty : new UsageTotals(row.InputTokens, row.OutputTokens, row.CostUsd, row.Calls);
    }

    public async ValueTask ResetAsync(string? consumer = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        if (consumer is null)
            await conn.ExecuteAsync(new CommandDefinition("DELETE FROM lyntai_usage",
                cancellationToken: ct)).ConfigureAwait(false);
        else
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM lyntai_usage WHERE lower(consumer) = lower(@consumer)",
                new { consumer }, cancellationToken: ct)).ConfigureAwait(false);
    }

}
