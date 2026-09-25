namespace Lyntai.Storage;

/// <summary>The dialect-neutral half of the relational <see cref="Lyntai.Inference.Budgeting.IUsageTracker"/>
/// backends, shared so the two cannot drift on how a consumer's spend is keyed or accumulated. The totals
/// read stays per backend: it is where the dialects genuinely differ (<c>CAST … AS REAL</c> against
/// <c>::bigint</c>, <c>COLLATE NOCASE</c> against <c>lower()</c>).</summary>
public static class UsageTrackerSql
{
    /// <summary>The consumer as stored and matched: case-folded with the rule <see cref="StringComparer.OrdinalIgnoreCase"/>
    /// applies in process, so a name outside ASCII — which a database's own case rules may not fold — is still
    /// one ledger. Bind every consumer through this; the SQL keeps its own case-insensitive match so a row
    /// stored before the fold still aggregates.</summary>
    /// <param name="consumer">The consumer as the caller named it.</param>
    public static string Consumer(string consumer)
    {
        ArgumentNullException.ThrowIfNull(consumer);
        return consumer.ToUpperInvariant();
    }

    /// <summary>Add one call's usage to the consumer's row, creating it on first use.</summary>
    public const string Record = """
        INSERT INTO lyntai_usage (consumer, input_tokens, output_tokens, cost_usd, calls)
        VALUES (@consumer, @in, @out, @cost, 1)
        ON CONFLICT (consumer) DO UPDATE SET
            input_tokens  = lyntai_usage.input_tokens  + @in,
            output_tokens = lyntai_usage.output_tokens + @out,
            cost_usd      = lyntai_usage.cost_usd      + @cost,
            calls         = lyntai_usage.calls         + 1
        """;

    /// <summary>Clear every consumer's ledger.</summary>
    public const string ResetAll = "DELETE FROM lyntai_usage";
}
