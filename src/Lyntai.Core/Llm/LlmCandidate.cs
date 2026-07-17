namespace Lyntai.Llm;

/// <summary>One entry in a router fallback list: which provider, optionally pinned to a model
/// (null lets the provider use its default).</summary>
public sealed record LlmCandidate(string ProviderId, string? Model = null);
