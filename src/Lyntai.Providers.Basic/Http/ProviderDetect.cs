namespace Lyntai.Providers.Http;

/// <summary>URL-shape detection, consulted at COMPOSITION (which provider class does this BaseUrl get?) and
/// for Azure's URL/auth conventions. Host matching is exact-or-subdomain, never substring (guards
/// <c>api.openai.com.evil.com</c>-style spoofing). Fail-open: anything unrecognized is the plain
/// OpenAI-shaped wire.</summary>
internal static class ProviderDetect
{
    /// <summary>Whether this base URL is an Ollama server ROOT — its well-known port with no <c>/v1</c>
    /// suffix — so <c>AddHttpProvider</c> composes the Ollama-native provider for it. A <c>/v1</c> base
    /// targets Ollama's OpenAI-shaped surface, where the native <c>/api/chat</c> payload/endpoint would 404
    /// on every call.</summary>
    internal static bool IsOllamaRoot(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri)
        && uri.Port == 11434
        && !uri.AbsolutePath.TrimEnd('/').EndsWith("/v1", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this base URL is an Azure OpenAI resource host, which carries two conventions the
    /// plain wire does not: the <c>/openai/v1</c> path prefix on a bare resource URL, and key auth in the
    /// <c>api-key</c> header. A custom domain fronting Azure sets
    /// <see cref="HttpModelOptions.AzureConventions"/> instead.</summary>
    internal static bool IsAzureHost(string? baseUrl) =>
        Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) && IsHost(uri.Host, "openai.azure.com");

    /// <summary>Exact host or a true subdomain — never a substring match.</summary>
    internal static bool IsHost(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
