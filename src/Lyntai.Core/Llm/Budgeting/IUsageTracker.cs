namespace Lyntai.Llm.Budgeting;

/// <summary>Accumulates token/cost usage across front-door calls, per consumer and globally. The
/// <c>AddUsageBudget</c> decorator records into it and reads it to enforce caps; the app can query totals
/// (what have I spent?) or reset them at a billing-window boundary. The built-in
/// <see cref="InMemoryUsageTracker"/> is the default — register your own <see cref="IUsageTracker"/>
/// before <c>AddUsageBudget</c> to persist/share spend across processes.</summary>
public interface IUsageTracker
{
    /// <summary>Record one call's usage under a consumer tag.</summary>
    void Record(string consumer, LlmUsage usage);

    /// <summary>Accumulated totals for <paramref name="consumer"/>, or the global total across all
    /// consumers when null.</summary>
    UsageTotals Total(string? consumer = null);

    /// <summary>Reset accumulated usage for <paramref name="consumer"/>, or everything when null.</summary>
    void Reset(string? consumer = null);
}

/// <summary>A snapshot of accumulated usage.</summary>
public sealed record UsageTotals(long InputTokens, long OutputTokens, double CostUsd, long Calls)
{
    public static readonly UsageTotals Empty = new(0, 0, 0, 0);

    /// <summary>Input + output tokens (what the token cap is measured against).</summary>
    public long TotalTokens => InputTokens + OutputTokens;
}
