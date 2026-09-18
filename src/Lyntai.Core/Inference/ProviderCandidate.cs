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
/// <para>The format is a promise every entry point makes (a DI builder's default order, a durable job's
/// payload, an agent tool's <c>backends</c> array), so it is parsed once: hand-written copies of the same
/// split are places for it to drift apart.</para></summary>
internal static class ProviderCandidateSpec
{
    /// <summary>Parse one spec. Both halves are trimmed, so a configuration string with spaces round the
    /// separator still resolves.</summary>
    public static ProviderCandidate Parse(string spec)
    {
        var at = spec.IndexOf(':');
        return at < 0
            ? new ProviderCandidate(spec.Trim())
            : new ProviderCandidate(spec[..at].Trim(), spec[(at + 1)..].Trim());
    }
}
