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
        var byo = httpClient is not null;
        if (byo)
        {
            resolveClient = sp => () => httpClient!(sp); // app-owned client + lifecycle — never disposed by Lyntai
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
            sp.GetService<ILogger<OpenAiCompatibleProvider>>(),
            disposeHttpClient: !byo)); // dispose only Lyntai-created clients
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
            o.Flavor = OpenAiFlavor.Ollama;
        }, httpClient);

    /// <summary>OpenRouter (openrouter.ai). Default id "openrouter".</summary>
    public static LyntaiBuilder AddOpenRouterProvider(this LyntaiBuilder builder, string apiKey,
        string? defaultModel = null, string id = "openrouter", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiCompatibleProvider(id, o =>
        {
            o.BaseUrl = "https://openrouter.ai/api/v1";
            o.ApiKey = apiKey;
            o.DefaultModel = defaultModel;
            o.Flavor = OpenAiFlavor.OpenRouter;
        }, httpClient);

    /// <summary>Azure OpenAI, targeting the resource's OpenAI-COMPATIBLE <c>v1</c> surface.
    /// <paramref name="endpoint"/> is your resource URL (e.g. <c>https://my-resource.openai.azure.com</c> —
    /// requests compose to <c>…/openai/v1/chat/completions</c>); <paramref name="apiKey"/> is sent as both
    /// the <c>api-key</c> header (Azure key auth) and a Bearer token. Default id "azure-openai".</summary>
    public static LyntaiBuilder AddAzureOpenAiProvider(this LyntaiBuilder builder, string endpoint, string apiKey,
        string? defaultModel = null, string id = "azure-openai", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiCompatibleProvider(id, o =>
        {
            o.BaseUrl = endpoint;
            o.ApiKey = apiKey;
            o.DefaultModel = defaultModel;
            o.Flavor = OpenAiFlavor.AzureOpenAi;
        }, httpClient);

    // ---- embeddings ------------------------------------------------------------------------------

    /// <summary>Register an <see cref="Lyntai.Embeddings.IEmbedder"/> over an OpenAI-compatible
    /// <c>/v1/embeddings</c> endpoint (OpenAI, LM Studio, OpenRouter, Azure) or Ollama's native batched
    /// <c>/api/embed</c> — enabling semantic memory (<see cref="Lyntai.Memory.ISemanticMemory"/>) without a
    /// BYO embedder. The flavor/endpoint is derived from <see cref="OpenAiCompatibleEmbedderOptions.BaseUrl"/>
    /// via the same <see cref="ProviderDetect"/> the chat provider uses, and the BYO-HttpClient seam is
    /// identical: pass <paramref name="httpClient"/> to own the client's lifecycle (else Lyntai registers a
    /// named client with an infinite HttpClient timeout so the per-call
    /// <see cref="LyntaiOptions.ProviderTimeout"/> owns deadlines). <paramref name="id"/> names the client
    /// and appears in error/log messages; there is one embedder slot, so a later registration wins.</summary>
    public static LyntaiBuilder AddOpenAiCompatibleEmbedder(this LyntaiBuilder builder, string id,
        Action<OpenAiCompatibleEmbedderOptions> configure, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        var config = new OpenAiCompatibleEmbedderOptions();
        configure(config);

        Func<IServiceProvider, Func<HttpClient>> resolveClient;
        var byo = httpClient is not null;
        if (byo)
        {
            resolveClient = sp => () => httpClient!(sp); // app-owned client + lifecycle — never disposed by Lyntai
        }
        else
        {
            builder.Services.AddHttpClient(EmbedderHttpClientName(id))
                .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);
            resolveClient = sp => () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(EmbedderHttpClientName(id));
        }

        builder.AddEmbeddings(sp => new HttpEmbedder(
            id,
            config,
            resolveClient(sp),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<HttpEmbedder>>(),
            disposeHttpClient: !byo)); // dispose only Lyntai-created clients
        return builder;
    }

    internal static string HttpClientName(string id) => $"lyntai.provider.{id}";
    internal static string EmbedderHttpClientName(string id) => $"lyntai.embedder.{id}";
}
