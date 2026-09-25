namespace Lyntai.Providers.Http;

/// <summary>
/// The URL and auth conventions every OpenAI-shaped surface in this package shares — the chat engine, the
/// vector transport and the rerank transport differ ONLY in their route names, so the rules live here once.
///
/// <para>Kept in one place deliberately: the Azure rule below is subtle, and when it lived in two copies a
/// drift would have been silent — chat would keep working while embeddings 404'd (or the reverse), with
/// nothing failing at build time to catch it.</para>
/// </summary>
internal static class HttpEndpoint
{
    /// <summary>Whether this registration follows Azure's resource conventions — the explicit
    /// <see cref="HttpModelOptions.AzureConventions"/> when set, otherwise derived from the BaseUrl host.
    /// Every consumer of the Azure rules should start here.</summary>
    internal static bool AzureFor(HttpModelOptions config) =>
        config.AzureConventions ?? ProviderDetect.IsAzureHost(config.BaseUrl);

    /// <summary>Compose the absolute endpoint for an OpenAI-shaped route.</summary>
    /// <param name="baseUrl">The configured base URL.</param>
    /// <param name="azureConventions">Whether Azure's resource conventions apply (see <see cref="AzureFor"/>).</param>
    /// <param name="openAiRoute">The route, UNrooted and without the version segment —
    /// e.g. <c>chat/completions</c>, <c>embeddings</c>, <c>rerank</c>.</param>
    internal static Uri Build(string baseUrl, bool azureConventions, string openAiRoute)
    {
        var b = baseUrl.TrimEnd('/');
        // Azure's OpenAI-shaped v1 surface lives under /openai/v1 on the resource host — a bare
        // resource URL (https://my-res.openai.azure.com) would otherwise compose /v1/… and 404. A base
        // that already includes /openai(…/v1) falls through to the generic suffix logic below.
        var path = azureConventions && !b.Contains("/openai", StringComparison.OrdinalIgnoreCase)
            ? $"/openai/v1/{openAiRoute}"
            : b.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? $"/{openAiRoute}" : $"/v1/{openAiRoute}";
        return new Uri(b + path);
    }

    /// <summary>Whether a call carries credentials — what separates NotConfigured from AuthFailed on a
    /// 401/403.</summary>
    internal static bool HasCredentials(string? apiKey) => !string.IsNullOrWhiteSpace(apiKey);

    /// <summary>Apply the request's auth headers. No key configured → no headers (a local llama-server or
    /// LM-Studio endpoint needs none). Azure key auth conventionally travels in the <c>api-key</c> header,
    /// and its v1 surface accepts either — so sending BOTH keeps the key path and a BYO Entra-token Bearer
    /// flow on one code path.</summary>
    internal static void ApplyAuth(HttpRequestMessage request, string? apiKey, bool azureConventions)
    {
        if (string.IsNullOrEmpty(apiKey)) return;
        request.Headers.Authorization = new("Bearer", apiKey);
        if (azureConventions)
            request.Headers.TryAddWithoutValidation("api-key", apiKey);
    }
}
