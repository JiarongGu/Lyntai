namespace Lyntai.Providers.Http;

/// <summary>Which wire dialect an HTTP endpoint speaks — the routes it exposes and the payload shape it
/// takes. Pin it on the options to force one; leave it <see cref="Auto"/> (the default) to detect it from
/// the BaseUrl.
///
/// <para><b>Not every member is OpenAI-compatible, which is why the family is not named for OpenAI</b>
/// (<c>docs/DECISIONS.md</c> D135). <see cref="OpenAi"/>, <see cref="OpenRouter"/> and
/// <see cref="AzureOpenAi"/> post OpenAI's schema; <see cref="Ollama"/> posts Ollama's own, and the two are
/// different endpoints with different bodies on the same server.</para></summary>
public enum HttpDialect
{
    /// <summary>Detect the dialect from the BaseUrl shape (see <see cref="ProviderDetect"/>). Detection is
    /// fail-open — an unrecognized URL is treated as plain <see cref="OpenAi"/>.</summary>
    Auto,

    /// <summary>Plain OpenAI-compatible: the OpenAI chat/embeddings schema over <c>/v1/…</c>.</summary>
    OpenAi,

    /// <summary>Ollama's native surface — the <c>/api/chat</c> + <c>/api/embed</c> endpoints and its
    /// <c>options.num_ctx</c> wire option (distinct from Ollama's separate OpenAI-COMPATIBLE <c>/v1</c>
    /// surface, which is plain <see cref="OpenAi"/>). Attachments travel as Ollama's own <c>images</c> array
    /// (base64, user turns only), where the OpenAI-shaped payload emits one <c>image_url</c> part instead.
    /// The one thing this schema cannot express is a <see cref="Lyntai.Llm.LlmAttachment"/> carrying only a
    /// remote <c>Uri</c> — <c>/api/chat</c> has no URL form and Lyntai will not fetch the bytes on your
    /// behalf, so such an attachment is REPORTED through the logger rather than sent.</summary>
    Ollama,

    /// <summary>OpenRouter. Currently behaves IDENTICALLY to <see cref="OpenAi"/> (no code path branches on
    /// it yet) — kept distinct so OpenRouter-specific behavior (e.g. its ranking headers) can land later
    /// without re-detecting, and so a pinned dialect stays honest.</summary>
    OpenRouter,

    /// <summary>Azure OpenAI. Its OpenAI-COMPATIBLE (v1) surface lives under <c>/openai/v1</c> on the
    /// resource host (a bare resource URL would 404 at <c>/v1/…</c>), and key auth conventionally travels in
    /// the <c>api-key</c> header — this dialect makes both adjustments.</summary>
    AzureOpenAi,
}
