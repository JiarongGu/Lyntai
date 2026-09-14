namespace Lyntai.Providers.Http;

/// <summary>Configuration for <see cref="CrossEncoderVerificationPolicy"/> — an
/// <see cref="Lyntai.Memory.Verification.IMemoryVerificationPolicy"/> backed by a cross-encoder over an
/// OpenAI/Cohere-shaped <c>/v1/rerank</c> endpoint (llama.cpp's <c>--reranking</c> mode, Cohere, Jina,
/// TEI).
///
/// <para><b>Verify your endpoint actually SCORES, because this seam cannot tell you that it does not.</b>
/// A backend whose scores have collapsed toward each other still returns a well-formed body, still yields
/// a full endorsement set, and reorders by noise — so a degraded deployment is indistinguishable from a
/// working one here, and counting distinct scores does not separate them. Check that your endpoint
/// separates a known-relevant from a known-irrelevant document by UNITS rather than by hundredths, against
/// the same model and serving stack you will run.</para>
///
/// <para><b>And when you cannot vet the backend, BOUND what it can cost instead of trusting it.</b>
/// <see cref="Lyntai.Memory.GraphMemoryOptions.VerdictCombination"/> set to
/// <see cref="Lyntai.Memory.Verification.MemoryVerdictCombination.Fuse"/> makes an endorsement COMPETE on
/// rank rather than replace the page. Measured with a cross-encoder in this seam it removes the partition's
/// whole <b>7.5-point</b> loss and adds nothing (<c>docs/memory-measurements.md</c> §5) — insurance rather
/// than an improvement, which is what a backend of unknown quality is worth paying for. It is the same
/// lever that doc offers for a weak LLM judge; a reranker is the other half of the same choice.</para>
/// </summary>
public sealed class CrossEncoderVerificationOptions
{
    /// <summary>Endpoint base, e.g. <c>http://localhost:8081</c>. A reranker is its own process: one
    /// <c>llama-server</c> serves ONE model, so this is a different endpoint from the chat or embedding
    /// one rather than another model behind the same port.</summary>
    public string BaseUrl { get; set; } = "http://localhost:8081";

    /// <summary>Sent as the <c>model</c> field. A server that has one model loaded ignores it — measured:
    /// it echoes back whatever it is sent — so it matters only on a router that selects by name.</summary>
    public string? Model { get; set; }

    /// <summary>Bearer token; null for keyless local endpoints.</summary>
    public string? ApiKey { get; set; }

    /// <summary>How many of the reranker's own best candidates to endorse. **Set this to the recall limit
    /// you ask for**, which is what every published figure used.
    ///
    /// <para><b>It is a fixed count on purpose, and the size is the whole design.</b> Under the shipped
    /// <c>Partition</c> combination an endorsed set is promoted ahead of everything unendorsed and then cut
    /// at the caller's limit — so endorsing exactly a page's worth makes the returned page BE the
    /// reranker's choice of the pool, and promotion REFINES the ranking. Endorsing more than a page
    /// REPLACES it instead, which is the measured failure of an LLM judge that endorsed 29.1 of 80.</para>
    ///
    /// <para>The library cannot default this for you: <c>MemoryVerificationRequest</c> carries the query and
    /// the candidates and deliberately not the caller's limit, so a policy cannot read it.</para></summary>
    public int EndorseCount { get; set; } = 20;
}
