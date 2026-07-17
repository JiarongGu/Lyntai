using Lyntai.Providers.OpenAiCompatible;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddOpenAiCompatibleProvider` shows up right on the builder.
namespace Lyntai;

public static class OpenAiCompatibleBuilderExtensions
{
    /// <summary>Register an OpenAI-compatible HTTP provider under <paramref name="id"/> (also usable
    /// multiple times with different ids — e.g. one "openai" and one "ollama").
    /// <para>BYO HttpClient: pass <paramref name="httpClient"/> to supply your own configured client
    /// (Polly resilience, auth handlers, a proxy, service discovery, or an existing named
    /// <see cref="IHttpClientFactory"/> client — e.g. <c>sp =&gt; sp.GetRequiredService&lt;IHttpClientFactory&gt;().CreateClient("my")</c>).
    /// You then own its timeout/lifecycle. When null (default), Lyntai registers a named client with an
    /// infinite HttpClient timeout so the per-call <see cref="LyntaiOptions.ProviderTimeout"/> owns deadlines.</para></summary>
    public static LyntaiBuilder AddOpenAiCompatibleProvider(this LyntaiBuilder builder, string id,
        Action<OpenAiCompatibleOptions> configure, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        var config = new OpenAiCompatibleOptions();
        configure(config);

        Func<IServiceProvider, Func<HttpClient>> resolveClient;
        if (httpClient is not null)
        {
            resolveClient = sp => () => httpClient(sp); // app-owned client + lifecycle
        }
        else
        {
            // per-call deadline (LyntaiOptions.ProviderTimeout) owns timeouts — not HttpClient's default 100s
            builder.Services.AddHttpClient(HttpClientName(id))
                .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);
            resolveClient = sp => () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName(id));
        }

        builder.AddProvider(sp => new OpenAiCompatibleProvider(
            id,
            config,
            resolveClient(sp),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<OpenAiCompatibleProvider>>()));
        return builder;
    }

    // ---- pre-configured presets ------------------------------------------------------------------
    // Thin wrappers over AddOpenAiCompatibleProvider with sensible defaults for common endpoints. Apps
    // that need something bespoke keep using AddOpenAiCompatibleProvider (or their own ILlmProvider via
    // builder.AddProvider). All presets accept a BYO httpClient like the base method.

    /// <summary>OpenAI (api.openai.com). Default id "openai".</summary>
    public static LyntaiBuilder AddOpenAiProvider(this LyntaiBuilder builder, string apiKey,
        string? defaultModel = null, string id = "openai", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiCompatibleProvider(id, o =>
        {
            o.BaseUrl = "https://api.openai.com";
            o.ApiKey = apiKey;
            o.DefaultModel = defaultModel;
        }, httpClient);

    /// <summary>A local (or remote) Ollama endpoint. Default base "http://localhost:11434", id "ollama".</summary>
    public static LyntaiBuilder AddOllamaProvider(this LyntaiBuilder builder, string? baseUrl = null,
        string? defaultModel = null, string id = "ollama", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiCompatibleProvider(id, o =>
        {
            o.BaseUrl = baseUrl ?? "http://localhost:11434";
            o.DefaultModel = defaultModel;
        }, httpClient);

    /// <summary>OpenRouter (openrouter.ai). Default id "openrouter".</summary>
    public static LyntaiBuilder AddOpenRouterProvider(this LyntaiBuilder builder, string apiKey,
        string? defaultModel = null, string id = "openrouter", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiCompatibleProvider(id, o =>
        {
            o.BaseUrl = "https://openrouter.ai/api/v1";
            o.ApiKey = apiKey;
            o.DefaultModel = defaultModel;
        }, httpClient);

    /// <summary>Azure OpenAI. <paramref name="endpoint"/> is your resource URL
    /// (e.g. https://my-resource.openai.azure.com). Default id "azure-openai".</summary>
    public static LyntaiBuilder AddAzureOpenAiProvider(this LyntaiBuilder builder, string endpoint, string apiKey,
        string? defaultModel = null, string id = "azure-openai", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiCompatibleProvider(id, o =>
        {
            o.BaseUrl = endpoint;
            o.ApiKey = apiKey;
            o.DefaultModel = defaultModel;
        }, httpClient);

    internal static string HttpClientName(string id) => $"lyntai.provider.{id}";
}
