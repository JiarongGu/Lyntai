namespace Lyntai.Providers.OpenAiCompatible;

/// <summary>URL-native detection: base-url shape → provider flavor. Fail-open: anything
/// unrecognized is treated as plain OpenAI-compatible. Host matching is exact-or-subdomain,
/// never substring (guards <c>api.openai.com.evil.com</c>-style spoofing).</summary>
public static class ProviderDetect
{
    public const string OpenAi = "openai";
    public const string Ollama = "ollama";
    public const string OpenRouter = "openrouter";

    public static string Detect(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl) ||
            !Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return OpenAi;

        if (IsHost(uri.Host, "openrouter.ai")) return OpenRouter;
        if (IsHost(uri.Host, "openai.com") || IsHost(uri.Host, "openai.azure.com")) return OpenAi;
        if (uri.Port == 11434) return Ollama; // Ollama's well-known port wherever it's hosted

        return OpenAi; // fail-open to OpenAI-compat
    }

    /// <summary>Exact host or a true subdomain — never a substring match.</summary>
    internal static bool IsHost(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
