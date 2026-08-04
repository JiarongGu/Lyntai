namespace Lyntai.Providers.OpenAiCompatible;

/// <summary>
/// The URL and auth conventions every OpenAI-compatible surface in this package shares — the chat provider
/// and the embedder differ ONLY in their route names, so the flavor rules live here once.
///
/// <para>Kept in one place deliberately: the Azure rule below is subtle, and when it lived in two copies a
/// drift would have been silent — chat would keep working while embeddings 404'd (or the reverse), with
/// nothing failing at build time to catch it.</para>
/// </summary>
internal static class OpenAiEndpoint
{
    /// <summary>Resolve the configured flavor, turning the <see cref="OpenAiFlavor.Auto"/> sentinel into a
    /// concrete flavor via URL detection. Every consumer of a flavor should start here.</summary>
    internal static OpenAiFlavor ResolveFlavor(OpenAiFlavor configured, string? baseUrl) =>
        configured == OpenAiFlavor.Auto ? ProviderDetect.Detect(baseUrl) : configured;

    /// <summary>Compose the absolute endpoint for a route.</summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <param name="flavor">A CONCRETE flavor (run it through <see cref="ResolveFlavor"/> first).</param>
    /// <param name="ollamaNativePath">Ollama's native, non-OpenAI path for this operation, rooted —
    /// e.g. <c>/api/chat</c>, <c>/api/embed</c>.</param>
    /// <param name="openAiRoute">The OpenAI-compatible route, UNrooted and without the version segment —
    /// e.g. <c>chat/completions</c>, <c>embeddings</c>.</param>
    internal static Uri Build(string baseUrl, OpenAiFlavor flavor, string ollamaNativePath, string openAiRoute)
    {
        var b = baseUrl.TrimEnd('/');
        var path = flavor switch
        {
            OpenAiFlavor.Ollama => ollamaNativePath,
            // Azure's OpenAI-COMPATIBLE (v1) surface lives under /openai/v1 on the resource host — a bare
            // resource URL (https://my-res.openai.azure.com) would otherwise compose /v1/… and 404. A base
            // that already includes /openai(…/v1) falls through to the generic suffix logic below.
            OpenAiFlavor.AzureOpenAi when !b.Contains("/openai", StringComparison.OrdinalIgnoreCase)
                => $"/openai/v1/{openAiRoute}",
            _ => b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? $"/{openAiRoute}" : $"/v1/{openAiRoute}",
        };
        return new Uri(b + path);
    }

    /// <summary>Apply the request's auth headers. No key configured → no headers (a local Ollama/LM-Studio
    /// endpoint needs none). Azure key auth conventionally travels in the <c>api-key</c> header, and its v1
    /// surface accepts either — so sending BOTH keeps the key path and a BYO Entra-token Bearer flow on one
    /// code path.</summary>
    internal static void ApplyAuth(HttpRequestMessage request, string? apiKey, OpenAiFlavor flavor)
    {
        if (string.IsNullOrEmpty(apiKey)) return;
        request.Headers.Authorization = new("Bearer", apiKey);
        if (flavor == OpenAiFlavor.AzureOpenAi)
            request.Headers.TryAddWithoutValidation("api-key", apiKey);
    }
}
