namespace Lyntai.Providers.Http;

/// <summary>URL-native detection: base-url shape → provider dialect. Fail-open: anything
/// unrecognized is treated as the plain OpenAI dialect. Host matching is exact-or-subdomain,
/// never substring (guards <c>api.openai.com.evil.com</c>-style spoofing).</summary>
internal static class ProviderDetect
{
    /// <summary>URL shape → a CONCRETE dialect. Never returns <see cref="HttpDialect.Auto"/> — that's the
    /// caller's "detect me" sentinel; this is the detection itself.</summary>
    internal static HttpDialect Detect(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return HttpDialect.OpenAi;

        if (IsHost(uri.Host, "openrouter.ai")) return HttpDialect.OpenRouter;
        if (IsHost(uri.Host, "openai.azure.com")) return HttpDialect.AzureOpenAi;
        if (IsHost(uri.Host, "openai.com")) return HttpDialect.OpenAi;
        // Ollama's well-known port — but a /v1 base targets its OpenAI-shaped surface, where the
        // native /api/chat payload/endpoint would 404 on every call
        if (uri.Port == 11434)
            return uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? HttpDialect.OpenAi : HttpDialect.Ollama;

        return HttpDialect.OpenAi; // fail-open to OpenAI-compat
    }

    /// <summary>Exact host or a true subdomain — never a substring match.</summary>
    internal static bool IsHost(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
