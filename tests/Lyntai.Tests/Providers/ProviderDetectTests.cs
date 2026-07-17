using Lyntai.Providers.OpenAiCompatible;

namespace Lyntai.Tests.Providers;

public class ProviderDetectTests
{
    [Theory]
    [InlineData("https://api.openai.com", "openai")]
    [InlineData("https://api.openai.com/v1", "openai")]
    [InlineData("https://myres.openai.azure.com", "openai")]
    [InlineData("https://openrouter.ai/api/v1", "openrouter")]
    [InlineData("http://localhost:11434", "ollama")]
    [InlineData("http://192.168.1.5:11434", "ollama")]
    [InlineData("http://gpu-box:11434", "ollama")]
    [InlineData("http://localhost:11434/v1", "openai")]   // Ollama's OpenAI-compatible surface
    [InlineData("http://localhost:11434/v1/", "openai")]
    [InlineData("https://my-own-gateway.example.com", "openai")]   // fail-open to OpenAI-compat
    [InlineData("not a url at all", "openai")]
    [InlineData("", "openai")]
    public void Detects_flavor_from_url_shape(string baseUrl, string expected)
    {
        Assert.Equal(expected, ProviderDetect.Detect(baseUrl));
    }

    [Theory]
    [InlineData("https://api.openai.com.evil.com")]   // substring spoof — must NOT match openai
    [InlineData("https://notopenai.com")]
    [InlineData("https://openrouter.ai.evil.net")]
    public void Host_match_is_exact_or_subdomain_never_substring(string spoofed)
    {
        Assert.Equal("openai", ProviderDetect.Detect(spoofed)); // fail-open bucket, not the spoofed brand
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
