namespace Lyntai.Providers.OpenAiCompatible;

/// <summary>URL-native detection: base-url shape → provider flavor. Fail-open: anything
/// unrecognized is treated as plain OpenAI-compatible. Host matching is exact-or-subdomain,
/// never substring (guards <c>api.openai.com.evil.com</c>-style spoofing).</summary>
internal static class ProviderDetect
{
    /// <summary>URL shape → a CONCRETE flavor. Never returns <see cref="OpenAiFlavor.Auto"/> — that's the
    /// caller's "detect me" sentinel; this is the detection itself.</summary>
    internal static OpenAiFlavor Detect(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return OpenAiFlavor.OpenAi;

        if (IsHost(uri.Host, "openrouter.ai")) return OpenAiFlavor.OpenRouter;
        if (IsHost(uri.Host, "openai.azure.com")) return OpenAiFlavor.AzureOpenAi;
        if (IsHost(uri.Host, "openai.com")) return OpenAiFlavor.OpenAi;
        // Ollama's well-known port — but a /v1 base targets its OpenAI-COMPATIBLE surface, where the
        // native /api/chat payload/endpoint would 404 on every call
        if (uri.Port == 11434)
            return uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? OpenAiFlavor.OpenAi : OpenAiFlavor.Ollama;

        return OpenAiFlavor.OpenAi; // fail-open to OpenAI-compat
    }

    /// <summary>Exact host or a true subdomain — never a substring match.</summary>
    internal static bool IsHost(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
