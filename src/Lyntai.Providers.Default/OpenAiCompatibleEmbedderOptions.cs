namespace Lyntai.Providers.OpenAiCompatible;

/// <summary>Configuration for <see cref="HttpEmbedder"/> — an <see cref="Lyntai.Embeddings.IEmbedder"/> over
/// an OpenAI-compatible <c>/v1/embeddings</c> endpoint (OpenAI, LM Studio, OpenRouter, Azure) or Ollama's
/// native batched <c>/api/embed</c>. The flavor is detected from <see cref="BaseUrl"/> (shared with the chat
/// provider's <see cref="ProviderDetect"/>) unless pinned.</summary>
public sealed class OpenAiCompatibleEmbedderOptions
{
    /// <summary>Endpoint base, e.g. <c>https://api.openai.com</c>, <c>http://localhost:11434</c>,
    /// <c>https://my-res.openai.azure.com</c>. The flavor is detected from this URL unless pinned.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>The embedding model, e.g. <c>text-embedding-3-small</c> / <c>nomic-embed-text</c>. Sent as
    /// the <c>model</c> field; endpoints that serve one loaded model (some LM Studio setups) ignore it.</summary>
    public string? Model { get; set; }

    /// <summary>Bearer token; null for keyless endpoints (local Ollama / LM Studio).</summary>
    public string? ApiKey { get; set; }

    /// <summary>Pin the payload flavor; <see cref="OpenAiFlavor.Auto"/> (default) detects it from
    /// <see cref="BaseUrl"/>.</summary>
    public OpenAiFlavor Flavor { get; set; } = OpenAiFlavor.Auto;

    /// <summary>Max inputs per HTTP request; a larger call list is split into this many at a time (real
    /// endpoints cap input counts — OpenAI at 2048, Azure historically at 16). <c>0</c> (default) sends the
    /// whole batch in a single request.</summary>
    public int BatchSize { get; set; }

    /// <summary>Prepended, VERBATIM, to text embedded as <see cref="Lyntai.Embeddings.EmbeddingRole.Document"/>
    /// — the storing side. Null or empty (the default) sends the text unchanged.
    ///
    /// <para><b>What this is for.</b> Asymmetric models want a different instruction per side and score
    /// materially worse without it. <b>The library supplies no default and knows no model's spelling</b>:
    /// which model you run, and what it wants prepended, is yours to set — <c>text-embedding-3-*</c> wants
    /// nothing here, the E5 family wants <c>"passage: "</c>, nomic <c>"search_document: "</c>.</para>
    ///
    /// <para><b>Including the trailing space, if the model wants one.</b> It is concatenated exactly as
    /// given; <c>"search_document:"</c> and <c>"search_document: "</c> are different inputs to the model.
    /// And changing either prefix changes every vector it produces, so a corpus embedded under one setting
    /// is not comparable to one embedded under another — re-index rather than mixing.</para></summary>
    public string? DocumentPrefix { get; set; }

    /// <summary>Prepended, VERBATIM, to text embedded as <see cref="Lyntai.Embeddings.EmbeddingRole.Query"/>
    /// — the searching side. Null or empty (the default) sends the text unchanged.
    /// <para>Set independently of <see cref="DocumentPrefix"/>: a model may instruct one side only, which is
    /// the BGE shape, and an unset side must stay verbatim rather than inherit the other.</para></summary>
    public string? QueryPrefix { get; set; }
}
