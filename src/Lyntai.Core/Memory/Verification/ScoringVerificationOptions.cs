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
    /// <summary>How many of the backend's own best candidates to endorse. **Set this to the recall limit
    /// you ask for**, which is what every published figure used.
    ///
    /// <para><b>It is a fixed count on purpose, and the size is the whole design.</b> Under the shipped
    /// <c>Partition</c> combination an endorsed set is promoted ahead of everything unendorsed and then cut
    /// at the caller's limit — so endorsing exactly a page's worth makes the returned page BE the backend's
    /// choice of the pool, and promotion REFINES the ranking. Endorsing more than a page REPLACES it
    /// instead, which is the measured failure of an LLM judge that endorsed 29.1 of 80.</para>
    ///
    /// <para>The library cannot default this for you: <see cref="MemoryVerificationRequest"/> carries the
    /// query and the candidates and deliberately not the caller's limit, so a policy cannot read it.</para></summary>
    public int EndorseCount { get; set; } = 20;
}
