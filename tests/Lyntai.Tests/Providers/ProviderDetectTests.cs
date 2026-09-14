using Lyntai.Providers.Http;

namespace Lyntai.Tests.Providers;

public class ProviderDetectTests
{
    [Theory]
    [InlineData("https://api.openai.com", HttpDialect.OpenAi)]
    [InlineData("https://api.openai.com/v1", HttpDialect.OpenAi)]
    [InlineData("https://myres.openai.azure.com", HttpDialect.AzureOpenAi)] // P3: Azure's own dialect (/openai/v1 path + api-key header)
    [InlineData("https://openrouter.ai/api/v1", HttpDialect.OpenRouter)]
    [InlineData("http://localhost:11434", HttpDialect.Ollama)]
    [InlineData("http://192.168.1.5:11434", HttpDialect.Ollama)]
    [InlineData("http://gpu-box:11434", HttpDialect.Ollama)]
    [InlineData("http://localhost:11434/v1", HttpDialect.OpenAi)]   // Ollama's OpenAI-compatible surface
    [InlineData("http://localhost:11434/v1/", HttpDialect.OpenAi)]
    [InlineData("https://my-own-gateway.example.com", HttpDialect.OpenAi)]   // fail-open to OpenAI-compat
    [InlineData("not a url at all", HttpDialect.OpenAi)]
    [InlineData("", HttpDialect.OpenAi)]
    public void Detects_flavor_from_url_shape(string baseUrl, HttpDialect expected)
    {
        Assert.Equal(expected, ProviderDetect.Detect(baseUrl));
    }

    [Fact]
    public void Detect_never_returns_Auto()
    {
        // Detect resolves to a CONCRETE dialect — Auto is the caller's "detect me" sentinel, never a result.
        foreach (var url in new[] { "https://api.openai.com", "http://localhost:11434", "garbage", "" })
            Assert.NotEqual(HttpDialect.Auto, ProviderDetect.Detect(url));
    }

    [Theory]
    [InlineData("https://api.openai.com.evil.com")]   // substring spoof — must NOT match openai
    [InlineData("https://notopenai.com")]
    [InlineData("https://openrouter.ai.evil.net")]
    public void Host_match_is_exact_or_subdomain_never_substring(string spoofed)
    {
        Assert.Equal(HttpDialect.OpenAi, ProviderDetect.Detect(spoofed)); // fail-open bucket, not the spoofed brand
    }

    [Fact]
    public void Subdomain_matching_helper_guards_the_edge()
    {
        Assert.True(ProviderDetect.IsHost("api.openai.com", "openai.com"));
        Assert.True(ProviderDetect.IsHost("openai.com", "openai.com"));
        Assert.False(ProviderDetect.IsHost("openai.com.evil.com", "openai.com"));
        Assert.False(ProviderDetect.IsHost("fakeopenai.com", "openai.com"));
    }
}
