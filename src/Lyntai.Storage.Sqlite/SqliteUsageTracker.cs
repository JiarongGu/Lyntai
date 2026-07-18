using Dapper;
using Lyntai.Llm;
using Lyntai.Llm.Budgeting;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IUsageTracker"/> — spend/token accounting that survives restarts (so a usage
/// budget isn't reset to zero every deploy). One row per consumer, incremented in place; the global total
/// is a SUM across rows. The interface is synchronous (it's called after a completion, off the hot path),
/// so this uses blocking Dapper calls. Register with <c>UseSqliteUsageTracking()</c>.
/// </summary>
public sealed class SqliteUsageTracker(IDbConnectionFactory factory) : IUsageTracker
{
    public void Record(string consumer, LlmUsage usage)
    {
        using var conn = factory.Open();
        conn.Execute("""
            INSERT INTO lyntai_usage (consumer, input_tokens, output_tokens, cost_usd, calls)
            VALUES (@consumer, @in, @out, @cost, 1)
            ON CONFLICT(consumer) DO UPDATE SET
                input_tokens  = input_tokens  + @in,
                output_tokens = output_tokens + @out,
                cost_usd      = cost_usd      + @cost,
                calls         = calls         + 1
            """, new { consumer, @in = usage.InputTokens, @out = usage.OutputTokens, cost = usage.CostUsd ?? 0 });
    }

    public UsageTotals Total(string? consumer = null)
    {
        using var conn = factory.Open();
        // CAST(... AS REAL): a whole-number cost sum comes back with integer affinity otherwise
        var row = consumer is null
            ? conn.QuerySingleOrDefault<Row>("""
                SELECT COALESCE(SUM(input_tokens),0) AS input_tokens, COALESCE(SUM(output_tokens),0) AS output_tokens,
                       CAST(COALESCE(SUM(cost_usd),0) AS REAL) AS cost_usd, COALESCE(SUM(calls),0) AS calls
                FROM lyntai_usage
                """)
            : conn.QuerySingleOrDefault<Row>("""
                SELECT input_tokens, output_tokens, CAST(cost_usd AS REAL) AS cost_usd, calls
                FROM lyntai_usage WHERE consumer = @consumer
                """, new { consumer });
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
