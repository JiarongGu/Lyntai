namespace Lyntai.Providers.OpenAiCompatible;

/// <summary>URL-native detection: base-url shape → provider flavor. Fail-open: anything
/// unrecognized is treated as plain OpenAI-compatible. Host matching is exact-or-subdomain,
/// never substring (guards <c>api.openai.com.evil.com</c>-style spoofing).</summary>
public static class ProviderDetect
{
    public const string OpenAi = "openai";
    public const string Ollama = "ollama";
    /// <summary>Detected for openrouter.ai hosts. Currently behaves IDENTICALLY to <see cref="OpenAi"/>
    /// (no code path branches on it yet) — kept distinct so OpenRouter-specific behavior (e.g. its ranking
    /// headers) can land later without re-detecting, and so a pinned flavor stays honest.</summary>
    public const string OpenRouter = "openrouter";
    /// <summary>Detected for *.openai.azure.com hosts. Azure's OpenAI-COMPATIBLE (v1) surface lives under
    /// <c>/openai/v1</c> on the resource host (a bare resource URL would 404 at <c>/v1/…</c>), and key auth
    /// conventionally travels in the <c>api-key</c> header — this flavor makes both adjustments.</summary>
    public const string AzureOpenAi = "azure-openai";

    internal static string Detect(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return OpenAi;

        if (IsHost(uri.Host, "openrouter.ai")) return OpenRouter;
        if (IsHost(uri.Host, "openai.azure.com")) return AzureOpenAi;
        if (IsHost(uri.Host, "openai.com")) return OpenAi;
        // Ollama's well-known port — but a /v1 base targets its OpenAI-COMPATIBLE surface, where the
        // native /api/chat payload/endpoint would 404 on every call
        if (uri.Port == 11434)
            return uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ? OpenAi : Ollama;

        return OpenAi; // fail-open to OpenAI-compat
    }

    /// <summary>Exact host or a true subdomain — never a substring match.</summary>
    internal static bool IsHost(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
