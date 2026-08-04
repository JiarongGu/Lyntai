namespace Lyntai.Media.Routing;

/// <summary>One routing candidate: WHICH backend, and optionally which of its models. A backend is not a
/// model — an aggregator serves hundreds behind one id, and the same model is often reachable through several
/// backends — so the pair is the unit of routing, exactly as on the LLM side.</summary>
/// <param name="ProviderId">The <see cref="IMediaProvider.Id"/> to use.</param>
/// <param name="Model">The model/endpoint id at that backend; null = the backend's default, or whatever the
/// request already named.</param>
public sealed record MediaCandidate(string ProviderId, string? Model = null);
