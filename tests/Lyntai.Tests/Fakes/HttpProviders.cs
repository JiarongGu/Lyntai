using Lyntai.Providers.Http;

namespace Lyntai.Tests.Fakes;

/// <summary>An OpenAI-shaped <see cref="HttpModelProvider"/> over a scripted handler — <c>using static</c> it.</summary>
public static class HttpProviders
{
    public static HttpModelProvider Provider(StubHttpHandler handler, Action<HttpModelOptions>? configure = null)
    {
        var config = new HttpModelOptions { BaseUrl = "https://api.openai.com", ApiKey = "test-key" };
        configure?.Invoke(config);
        return new HttpModelProvider("openai", config, () => new HttpClient(handler, disposeHandler: false),
            new LyntaiOptions { ProviderTimeout = TimeSpan.FromSeconds(30) });
    }
}
