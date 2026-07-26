using Dapper;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IUsageTracker"/> — spend/token accounting that survives restarts (so a usage
/// budget isn't reset to zero every deploy). One row per consumer, incremented in place; totals are SUMs
/// across rows. Fully async: <c>TotalAsync</c> is a PRE-CALL read on every budgeted request, so it must
/// not block a threadpool thread inside the async front door. Register with <c>UseSqliteUsageTracking()</c>.
/// </summary>
public sealed class SqliteUsageTracker(IDbConnectionFactory factory) : IUsageTracker
{
    public async ValueTask RecordAsync(string consumer, LlmUsage usage, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_usage (consumer, input_tokens, output_tokens, cost_usd, calls)
            VALUES (@consumer, @in, @out, @cost, 1)
            ON CONFLICT(consumer) DO UPDATE SET
                input_tokens  = input_tokens  + @in,
                output_tokens = output_tokens + @out,
                cost_usd      = cost_usd      + @cost,
                calls         = calls         + 1
            """, new { consumer, @in = usage.InputTokens, @out = usage.OutputTokens, cost = usage.CostUsd ?? 0 },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    public async ValueTask<UsageTotals> TotalAsync(string? consumer = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // CAST(... AS REAL): a whole-number cost sum comes back with integer affinity otherwise.
        // Per-consumer: SUM + COLLATE NOCASE — rows keep their exact casing (the TEXT PK), but consumer
        // identity is case-insensitive library-wide, so the total AGGREGATES across casings.
        var row = consumer is null
            ? await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
                SELECT COALESCE(SUM(input_tokens),0) AS input_tokens, COALESCE(SUM(output_tokens),0) AS output_tokens,
                       CAST(COALESCE(SUM(cost_usd),0) AS REAL) AS cost_usd, COALESCE(SUM(calls),0) AS calls
                FROM lyntai_usage
                """, cancellationToken: ct)).ConfigureAwait(false)
            : await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition("""
                SELECT COALESCE(SUM(input_tokens),0) AS input_tokens, COALESCE(SUM(output_tokens),0) AS output_tokens,
                       CAST(COALESCE(SUM(cost_usd),0) AS REAL) AS cost_usd, COALESCE(SUM(calls),0) AS calls
                FROM lyntai_usage WHERE consumer = @consumer COLLATE NOCASE
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
                "DELETE FROM lyntai_usage WHERE consumer = @consumer COLLATE NOCASE",
                new { consumer }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private sealed class Row
    {
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public double CostUsd { get; set; }
        public long Calls { get; set; }
    }
}
