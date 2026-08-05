namespace Lyntai.Llm;

/// <summary>One entry in a router fallback list: which provider, optionally pinned to a model
/// (null lets the provider use its default).
/// <para><see cref="ProviderId"/> is matched CASE-INSENSITIVELY against the provider's own
/// <see cref="ILlmProvider.Id"/>, the same way the generation router and the lifecycle subsystem match an id
/// — so <c>"openai"</c> and <c>"OpenAI"</c> select the same backend, and naming both in one list is ONE
/// candidate, not two. <see cref="Model"/> is compared ordinally where it matters: a model id is a vendor's
/// opaque string, and two casings of one are not reliably the same endpoint.</para></summary>
public sealed record LlmCandidate(string ProviderId, string? Model = null);
