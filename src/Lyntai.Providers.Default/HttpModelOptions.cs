using Lyntai.Lifecycle;

namespace Lyntai.Providers.Http;

/// <summary>One BACKEND served over HTTP: an endpoint, a dialect, a model, and what that model puts out.
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
    /// <summary>Endpoint base, e.g. <c>https://api.openai.com</c>, <c>http://localhost:11434</c>,
    /// <c>https://openrouter.ai/api/v1</c>. The dialect is detected from this URL unless pinned.</summary>
    public string BaseUrl { get; set; } = "https://api.openai.com";

    /// <summary>Bearer token; null for keyless endpoints (local Ollama, LM Studio, llama-server).</summary>
    public string? ApiKey { get; set; }

    /// <summary>The model this backend serves, e.g. <c>gpt-4o</c>, <c>llama3.1</c>,
    /// <c>text-embedding-3-small</c>. Used when neither the request nor the candidate pins one.</summary>
    public string? Model { get; set; }

    /// <summary>Pin the payload dialect; <see cref="HttpDialect.Auto"/> (default) detects it from BaseUrl.</summary>
    public HttpDialect Dialect { get; set; } = HttpDialect.Auto;

    /// <summary>What this backend puts out — <see cref="ProviderKinds.Text"/> (default) posts to
    /// <c>/chat/completions</c>, <see cref="ProviderKinds.Vector"/> to <c>/embeddings</c>. It is the field
    /// that decides the route, the wire shape, and which methods the provider answers.
    ///
    /// <para>A single value rather than a list, because one registration is one backend. A LIST on
    /// <see cref="ProviderCapabilities.Produces"/> means something else and still holds: one CALL returning
    /// several kinds at once, as a multimodal model emitting text and an image does. Two endpoints behind
    /// one hostname is not that.</para></summary>
    public string Produces { get; set; } = ProviderKinds.Text;

    /// <summary>Context-window override for <see cref="HttpDialect.Ollama"/> serving
    /// <see cref="ProviderKinds.Text"/> ONLY — it becomes Ollama's <c>options.num_ctx</c> on the native
    /// <c>/api/chat</c> payload. **Every other dialect IGNORES it silently**: the OpenAI-shaped payload has
    /// no equivalent knob (the context window is a property of the deployed model there), and that includes
    /// Ollama's own OpenAI-COMPATIBLE <c>/v1</c> surface, which resolves to
    /// <see cref="HttpDialect.OpenAi"/>. The name carries the backend for exactly that reason — a generic
    /// one read as a portable setting and was not one.</summary>
    public int? OllamaContextSize { get; set; }

    /// <summary>Max inputs per HTTP request when serving <see cref="ProviderKinds.Vector"/>; a larger call
    /// list is split into this many at a time (real endpoints cap input counts — OpenAI at 2048, Azure
    /// historically at 16). <c>0</c> (default) sends the whole batch in a single request.</summary>
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
