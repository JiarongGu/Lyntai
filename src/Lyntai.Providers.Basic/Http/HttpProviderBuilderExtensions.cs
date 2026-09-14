using Lyntai.Lifecycle;
using Lyntai.Providers.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddHttpProvider` shows up right on the builder.
namespace Lyntai;

public static class HttpProviderBuilderExtensions
{
    /// <summary>Register a model served over HTTP under <paramref name="id"/>, in whichever
    /// <see cref="HttpModelOptions.Dialect"/> the endpoint speaks (also usable multiple times with
    /// different ids — e.g. one "openai" and one "ollama", or a chat and an embedding backend on one
    /// server).
    /// <para><b>This one KEEPS the <c>Provider</c> suffix</b> where a named backend drops it (<b>D134</b>):
    /// like <see cref="LyntaiBuilder.AddProvider(Func{IServiceProvider,Lyntai.Lifecycle.IModelProvider})"/>
    /// it is the GENERIC registration, so <c>Provider</c> is the noun it takes rather than a suffix on a
    /// vendor's name. The vendor presets below — <see cref="AddOpenAiProvider"/>, <see cref="AddOllamaProvider"/> — name a
    /// backend, so they do not carry it.</para>
    /// <para>BYO HttpClient: pass <paramref name="httpClient"/> to supply your own configured client
    /// (Polly resilience, auth handlers, a proxy, service discovery, or an existing named
    /// <see cref="IHttpClientFactory"/> client — e.g. <c>sp =&gt; sp.GetRequiredService&lt;IHttpClientFactory&gt;().CreateClient("my")</c>).
    /// You then own its timeout/lifecycle. When null (default), Lyntai registers a named client with an
    /// infinite HttpClient timeout so the per-call <see cref="LyntaiOptions.ProviderTimeout"/> owns deadlines.</para></summary>
    public static LyntaiBuilder AddHttpProvider(this LyntaiBuilder builder, string id,
        Action<HttpModelOptions> configure, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        var config = new HttpModelOptions();
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

        HttpModelProvider Build(IServiceProvider sp) => new(
            id,
            config,
            resolveClient(sp),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<HttpModelProvider>>(),
            disposeHttpClient: !byo); // dispose only Lyntai-created clients

        // What it PRODUCES picks the front door. AddEmbeddingProvider is the same collection plus the
        // statement that something can embed, which AddSemanticMemory and the routed IEmbedder read at
        // composition time, before any provider is built (D129).
        if (string.Equals(config.Produces, ProviderKinds.Vector, StringComparison.OrdinalIgnoreCase))
            builder.AddEmbeddingProvider(Build);
        else
            builder.AddProvider(Build);
        return builder;
    }

    // ---- pre-configured presets ------------------------------------------------------------------
    // Thin wrappers over AddHttpProvider with sensible defaults for common endpoints. Apps
    // that need something bespoke keep using AddHttpProvider (or their own IModelProvider via
    // builder.AddProvider). All presets accept a BYO httpClient like the base method.

    /// <summary>OpenAI (api.openai.com). Default id "openai".</summary>
    public static LyntaiBuilder AddOpenAiProvider(this LyntaiBuilder builder, string apiKey,
        string? model = null, string id = "openai", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddHttpProvider(id, o =>
        {
            o.BaseUrl = "https://api.openai.com";
            o.ApiKey = apiKey;
            o.Model = model;
        }, httpClient);

    /// <summary>A local (or remote) Ollama endpoint, pinned to Ollama's NATIVE surface
    /// (<see cref="HttpDialect.Ollama"/>). Default base "http://localhost:11434", id "ollama".
    /// <para><paramref name="baseUrl"/> must be the server ROOT (e.g. <c>http://localhost:11434</c>), never
    /// its <c>/v1</c> OpenAI-compatible surface: the pin is applied over whatever URL you pass, so a
    /// <c>/v1</c> base composes to <c>…/v1/api/chat</c> and 404s on the first call. Send a <c>/v1</c> base
    /// through <see cref="AddHttpProvider"/> instead, whose detection resolves it correctly.</para>
    /// <para>Attachments are carried: an <see cref="Lyntai.Llm.LlmAttachment"/> with <c>Data</c> travels in
    /// Ollama's own <c>images</c> array on a user turn (pair it with a vision model — <c>llava</c> and
    /// friends). An attachment carrying only a remote <c>Uri</c> is the one shape this endpoint cannot take,
    /// since <c>/api/chat</c> has no URL form; it is logged as undeliverable rather than dropped in
    /// silence.</para></summary>
    public static LyntaiBuilder AddOllamaProvider(this LyntaiBuilder builder, string? baseUrl = null,
        string? model = null, string id = "ollama", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddHttpProvider(id, o =>
        {
            o.BaseUrl = baseUrl ?? "http://localhost:11434";
            o.Model = model;
            o.Dialect = HttpDialect.Ollama;
        }, httpClient);

    /// <summary>A local (or remote) llama.cpp <c>llama-server</c>, which speaks the plain OpenAI schema off
    /// its ROOT (<see cref="HttpDialect.OpenAi"/>). Default base "http://localhost:8080", id "llama",
    /// keyless.
    /// <para><b>What <paramref name="model"/> means here is not what it means on a catalogue
    /// endpoint.</b> A <c>llama-server</c> started with <c>--model</c> serves exactly ONE model and answers
    /// to whatever <c>--alias</c> names it, so the model on a request is a LABEL and a wrong one is not an
    /// error — you get the loaded model either way. It selects only on a router server
    /// (<c>--models-dir</c>), where the name must match an entry. Pass <see langword="null"/> unless you run
    /// a router or want the label recorded on traces.</para>
    /// <para>Pass the server ROOT, not its <c>/v1</c>: requests compose to <c>…/v1/chat/completions</c>.
    /// Unlike <see cref="AddOllamaProvider"/> there is no native surface to pin — <c>llama-server</c> has
    /// only the OpenAI-shaped one — so an attachment travels as an <c>image_url</c> part and a remote
    /// <c>Uri</c> attachment is deliverable, which Ollama's own schema cannot express.</para></summary>
    public static LyntaiBuilder AddLlamaProvider(this LyntaiBuilder builder, string? baseUrl = null,
        string? model = null, string id = "llama", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddHttpProvider(id, o =>
        {
            o.BaseUrl = baseUrl ?? "http://localhost:8080";
            o.Model = model;
            o.Dialect = HttpDialect.OpenAi;
        }, httpClient);

    /// <summary>OpenRouter (openrouter.ai). Default id "openrouter".</summary>
    public static LyntaiBuilder AddOpenRouterProvider(this LyntaiBuilder builder, string apiKey,
        string? model = null, string id = "openrouter", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddHttpProvider(id, o =>
        {
            o.BaseUrl = "https://openrouter.ai/api/v1";
            o.ApiKey = apiKey;
            o.Model = model;
            o.Dialect = HttpDialect.OpenRouter;
        }, httpClient);

    /// <summary>Azure OpenAI, targeting the resource's OpenAI-COMPATIBLE <c>v1</c> surface.
    /// <paramref name="endpoint"/> is your resource URL (e.g. <c>https://my-resource.openai.azure.com</c> —
    /// requests compose to <c>…/openai/v1/chat/completions</c>); <paramref name="apiKey"/> is sent as both
    /// the <c>api-key</c> header (Azure key auth) and a Bearer token. Default id "azure-openai".</summary>
    public static LyntaiBuilder AddAzureOpenAiProvider(this LyntaiBuilder builder, string endpoint, string apiKey,
        string? model = null, string id = "azure-openai", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddHttpProvider(id, o =>
        {
            o.BaseUrl = endpoint;
            o.ApiKey = apiKey;
            o.Model = model;
            o.Dialect = HttpDialect.AzureOpenAi;
        }, httpClient);

    // One name, because there is one registration per host now — an embeddings-only host is a provider with
    // Chat nulled, so it takes the provider client like any other (D132).
    internal static string HttpClientName(string id) => $"lyntai.provider.{id}";
}
