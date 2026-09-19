using Lyntai.Inference;

namespace Lyntai.Providers.Http;

/// <summary>One BACKEND served over HTTP in the OpenAI-shaped schema: an endpoint, a model, and what that
/// model puts out. A backend speaking its OWN wire schema is its own provider with its own options —
/// Ollama-native is <c>OllamaOptions</c> one namespace over (<c>docs/DECISIONS.md</c> D160).
///
/// <para><b>One registration is one backend, and a shared hostname does not make two of them one.</b> A
/// chat model and an embedding model are different models at different routes with different wire shapes;
/// the only thing they share is a base URL and a key, which is TRANSPORT rather than identity. A host
/// serving both is registered twice, under two ids, so a trace can say which one answered
/// (<c>docs/DECISIONS.md</c> D133).</para>
///
/// <para><b>There are no route sub-objects here on purpose.</b> A <c>Chat</c> section beside an
/// <c>Embeddings</c> section is a hardcoded taxonomy of exactly two kinds, living inside the object whose
/// whole premise is that <see cref="ProviderKinds"/> is an OPEN list — a reranking host would have grown a
/// third, a rendering host a fourth. <see cref="Produces"/> is one field because a registration serves one
/// kind.</para></summary>
public sealed class HttpModelOptions
{
    /// <summary>Endpoint base, e.g. <c>https://api.openai.com</c>, <c>http://localhost:8080</c>,
    /// <c>https://openrouter.ai/api/v1</c>. Requests compose the <c>/v1</c> convention over it (a base
    /// already ending in <c>/v1</c> is not doubled). An Ollama server ROOT given to <c>AddHttpProvider</c>
    /// composes the Ollama-native provider instead — see <c>AddOllamaProvider</c>.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>Bearer token; null for keyless endpoints (LM Studio, llama-server).</summary>
    public string? ApiKey { get; set; }

    /// <summary>The model this backend serves, e.g. <c>gpt-4o</c>, <c>text-embedding-3-small</c>. Used when
    /// neither the request nor the candidate pins one.</summary>
    public string? Model { get; set; }

    /// <summary>Whether Azure's resource conventions apply: the OpenAI-shaped v1 surface under
    /// <c>/openai/v1</c> on a bare resource URL, and key auth in the <c>api-key</c> header (sent beside the
    /// Bearer token, so a BYO Entra-token flow shares one code path). Null (the default) derives it from the
    /// BaseUrl host (<c>*.openai.azure.com</c>); set <see langword="true"/> for a custom domain fronting an
    /// Azure resource, or <see langword="false"/> to suppress the conventions on an Azure-looking host.</summary>
    public bool? AzureConventions { get; set; }

    /// <summary>What this backend puts out — <see cref="ProviderKinds.Text"/> (default) posts to
    /// <c>chat/completions</c>, <see cref="ProviderKinds.Vector"/> to <c>embeddings</c>,
    /// <see cref="ProviderKinds.Score"/> to <c>rerank</c> (the route Cohere defined and llama.cpp's
    /// <c>--reranking</c> mode serves). It is the field that decides the route, the wire shape, and which
    /// methods the provider answers.
    ///
    /// <para>A single value rather than a list, because one registration is one backend. A LIST on
    /// <see cref="ProviderCapabilities.Produces"/> means something else and still holds: one CALL returning
    /// several kinds at once, as a multimodal model emitting text and an image does. Two endpoints behind
    /// one hostname is not that.</para></summary>
    public string Produces { get; set; } = ProviderKinds.Text;

    /// <summary>Max inputs per HTTP request when serving <see cref="ProviderKinds.Vector"/>; a larger call
    /// list is split into this many at a time (real endpoints cap input counts — OpenAI at 2048, Azure
    /// historically at 16). <c>0</c> (default) sends the whole batch in a single request.</summary>
    public int BatchSize { get; set; }

    /// <summary>Prepended, VERBATIM, to text embedded as <see cref="Lyntai.Inference.EmbeddingRole.Document"/>
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

    /// <summary>Prepended, VERBATIM, to text embedded as <see cref="Lyntai.Inference.EmbeddingRole.Query"/>
    /// — the searching side. Null or empty (the default) sends the text unchanged.
    /// <para>Set independently of <see cref="DocumentPrefix"/>: a model may instruct one side only, which is
    /// the BGE shape, and an unset side must stay verbatim rather than inherit the other.</para></summary>
    public string? QueryPrefix { get; set; }
}
