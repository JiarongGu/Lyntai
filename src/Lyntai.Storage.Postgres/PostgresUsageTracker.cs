using Dapper;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;

namespace Lyntai.Storage.Postgres;

/// <summary>
/// PostgreSQL-backed <see cref="IUsageTracker"/> — spend/token accounting that survives restarts and can be
/// shared across processes. One row per consumer, incremented in place; the global total is a SUM across
/// rows. The interface is synchronous (called after a completion, off the hot path), so this uses blocking
/// Dapper calls. Register with <c>UsePostgresUsageTracking()</c>.
/// </summary>
public sealed class PostgresUsageTracker(IDbConnectionFactory factory) : IUsageTracker
{
    public void Record(string consumer, LlmUsage usage)
    {
        using var conn = factory.Open();
        conn.Execute("""
            INSERT INTO lyntai_usage (consumer, input_tokens, output_tokens, cost_usd, calls)
            VALUES (@consumer, @in, @out, @cost, 1)
            ON CONFLICT (consumer) DO UPDATE SET
                input_tokens  = lyntai_usage.input_tokens  + @in,
                output_tokens = lyntai_usage.output_tokens + @out,
                cost_usd      = lyntai_usage.cost_usd      + @cost,
                calls         = lyntai_usage.calls         + 1
            """, new { consumer, @in = usage.InputTokens, @out = usage.OutputTokens, cost = usage.CostUsd ?? 0 });
    }

    public UsageTotals Total(string? consumer = null)
    {
        using var conn = factory.Open();
        var row = consumer is null
            ? conn.QuerySingleOrDefault<Row>("""
                SELECT COALESCE(SUM(input_tokens),0)::bigint AS input_tokens,
                       COALESCE(SUM(output_tokens),0)::bigint AS output_tokens,
                       COALESCE(SUM(cost_usd),0)::double precision AS cost_usd,
                       COALESCE(SUM(calls),0)::bigint AS calls
                FROM lyntai_usage
                """)
            : conn.QuerySingleOrDefault<Row>(
                "SELECT input_tokens, output_tokens, cost_usd, calls FROM lyntai_usage WHERE consumer = @consumer", new { consumer });
        return row is null ? UsageTotals.Empty : new UsageTotals(row.InputTokens, row.OutputTokens, row.CostUsd, row.Calls);
    }

    public void Reset(string? consumer = null)
    {
        using var conn = factory.Open();
        if (consumer is null) conn.Execute("DELETE FROM lyntai_usage");
        else conn.Execute("DELETE FROM lyntai_usage WHERE consumer = @consumer", new { consumer });
    }

    private sealed class Row
    {
        public long InputTokens { get; set; }
        public long OutputTokens { get; set; }
        public double CostUsd { get; set; }
        public long Calls { get; set; }
    }
}
