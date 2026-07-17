namespace Lyntai.Llm;

/// <summary>Token/cost accounting for one call, as reported by the provider.</summary>
public sealed record LlmUsage(long InputTokens, long OutputTokens, long CacheReadTokens = 0, double? CostUsd = null);
