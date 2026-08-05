namespace Lyntai.Generation.Routing;

/// <summary>One routing candidate: WHICH backend, and optionally which of its models. A backend is not a
/// model — an aggregator serves hundreds behind one id, and the same model is often reachable through several
/// backends — so the pair is the unit of routing, exactly as on the LLM side.</summary>
/// <param name="ProviderId">The <see cref="IGenerationProvider"/>'s <see cref="Lyntai.Lifecycle.IProviderIdentity.Id"/> to use.</param>
/// <param name="Model">The model/endpoint id at that backend; null = the backend's default, or whatever the
/// request already named.</param>
public sealed record GenerationCandidate(string ProviderId, string? Model = null);

/// <summary>The one place a candidate SPEC — <c>"provider"</c> or <c>"provider:model"</c> — is read. The format
/// is a promise every entry point makes (the DI builder's default order, a durable job's payload, the agent
/// tools' <c>backends</c> array), so it is parsed once: three hand-written copies of the same split are three
/// places for it to drift apart.</summary>
internal static class GenerationCandidateSpec
{
    /// <summary>Parse one spec. Both halves are trimmed, so a configuration string with spaces round the
    /// separator still resolves.</summary>
    public static GenerationCandidate Parse(string spec)
    {
        var at = spec.IndexOf(':');
        return at < 0
            ? new GenerationCandidate(spec.Trim())
            : new GenerationCandidate(spec[..at].Trim(), spec[(at + 1)..].Trim());
    }
}
