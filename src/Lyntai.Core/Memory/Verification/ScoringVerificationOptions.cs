namespace Lyntai.Memory.Verification;

/// <summary>Knobs for <see cref="ScoringVerificationPolicy"/> — the memory half of verifying with a
/// scoring backend. What the backend IS, and where it lives, is the provider's configuration; this is only
/// what the engine does with the scores it gets back.
///
/// <para><b>When you cannot vet the backend, BOUND what it can cost instead of trusting it.</b>
/// <see cref="Lyntai.Memory.GraphMemoryOptions.VerdictCombination"/> set to
/// <see cref="MemoryVerdictCombination.Fuse"/> makes an endorsement COMPETE on rank rather than replace the
/// page. Measured with a cross-encoder in this seam it removes the partition's whole <b>7.5-point</b> loss
/// and adds nothing (<c>docs/memory-measurements.md</c> §5) — insurance rather than an improvement, which is
/// what a backend of unknown quality is worth paying for. It is the same lever that doc offers for a weak
/// LLM judge; a reranker is the other half of the same choice.</para></summary>
public sealed class ScoringVerificationOptions
{
    /// <summary>WHICH registered backend scores, by <see cref="Lyntai.Lifecycle.IModelProvider.Id"/>. Null —
    /// the default — takes the first registered one that produces <see cref="Lyntai.Lifecycle.ProviderKinds.Score"/>, which
    /// is what a deployment with exactly one wants and what this seam did before the option existed.
    ///
    /// <para><b>Name it as soon as a second scoring backend exists for ANY reason.</b> Under the default,
    /// registration ORDER decides what verifies memory and nothing reports the choice — so a backend added
    /// for a tool selector or a ranking policy silently becomes the memory verifier too. The two
    /// model-backed sibling seams say it with
    /// <see cref="LlmVerificationOptions.ClientName"/>; this is the same lever over a provider id rather
    /// than a client name, because a scoring backend is selected by what it PRODUCES and never routed.</para>
    ///
    /// <para>An id naming no registered backend — or one that does not declare
    /// <see cref="Lyntai.Lifecycle.ProviderKinds.Score"/> — THROWS when the policy is composed, rather than reporting
    /// <c>NoOpinion</c> on every recall, which is the silent degradation this option exists to remove.
    /// Matched case-insensitively, like every other id lookup here.</para></summary>
    public string? ProviderId { get; set; }

    /// <summary>How many of the backend's own best candidates to endorse. <b>Set it to the recall limit you
    /// ask for</b>, which is what every published figure used.
    ///
    /// <para>A fixed count is the design: endorsing a page's worth makes promotion REFINE the ranking, and
    /// endorsing more REPLACES it — the measured failure of an LLM judge that endorsed 29.1 of 80.
    /// <c>docs/memory.md</c> carries the mechanism.</para>
    ///
    /// <para>The library cannot default it for you: <see cref="MemoryVerificationRequest"/> deliberately
    /// does not carry the caller's limit, so a policy cannot read it.</para></summary>
    public int EndorseCount { get; set; } = 20;
}
