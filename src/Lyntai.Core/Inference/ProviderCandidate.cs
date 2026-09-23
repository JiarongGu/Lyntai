namespace Lyntai.Inference;

/// <summary>One entry in a router's fallback list: WHICH backend, and optionally which of its models.
///
/// <para><b>A backend is not a model</b> — an aggregator serves hundreds behind one id, and the same model
/// is often reachable through several backends — so the pair is the unit of routing in every domain.</para>
///
/// <para><b>One type for every provider seam.</b> It replaces two byte-identical records — one per domain —
/// whose own documentation admitted the second behaved "exactly as on the LLM side". Two copies of a shared
/// rule drift; one cannot (<c>docs/DECISIONS.md</c> D125).</para>
/// </summary>
/// <param name="ProviderId">Matched CASE-INSENSITIVELY against the backend's
/// <see cref="IProviderIdentity.Id"/>, the same way the lifecycle subsystem matches one — so <c>"openai"</c>
/// and <c>"OpenAI"</c> select the same backend, and naming both in one list is ONE candidate, not two.</param>
/// <param name="Model">The model id at that backend; null means the backend's default, or whatever the
/// request already named. Compared ORDINALLY: a model id is a vendor's opaque string, and two casings of one
/// are not reliably the same endpoint.</param>
public sealed record ProviderCandidate(string ProviderId, string? Model = null);

/// <summary>The one place a candidate SPEC — <c>"provider"</c> or <c>"provider:model"</c> — is read.
///
/// <para>The format is a promise every entry point makes (a DI builder's default order,
/// <c>LYNTAI_DEFAULT_CANDIDATES</c>, a live route, a durable job's payload, an agent tool's <c>backends</c>
/// array), so it is parsed once: hand-written copies of the same split are places for it to drift apart.</para></summary>
internal static class ProviderCandidateSpec
{
    /// <summary>Parse one spec, split at the FIRST colon (a model id may contain one). Both halves are trimmed,
    /// so a configuration string with spaces round the separator still resolves, and a blank model is null —
    /// the backend's default — because an empty one would outrank the request's own model.</summary>
    public static ProviderCandidate Parse(string spec)
    {
        var at = spec.IndexOf(':');
        if (at < 0) return new ProviderCandidate(spec.Trim());
        var model = spec[(at + 1)..].Trim();
        return new ProviderCandidate(spec[..at].Trim(), model.Length == 0 ? null : model);
    }

    /// <summary>Write one candidate back as the spec <see cref="Parse"/> reads.</summary>
    public static string Format(ProviderCandidate candidate) =>
        candidate.Model is null ? candidate.ProviderId : $"{candidate.ProviderId}:{candidate.Model}";
}
