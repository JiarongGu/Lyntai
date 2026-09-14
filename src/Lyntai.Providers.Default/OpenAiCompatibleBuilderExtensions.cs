using Lyntai.Lifecycle;
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

        // Declaring embeddings on THIS host says they live on it: anything left blank is the host's own.
        // Model is deliberately absent — DefaultModel names a chat model (OpenAiCompatibleOptions.Embeddings).
        if (config.Embeddings is { } embeddings)
        {
            embeddings.BaseUrl ??= config.BaseUrl;
            embeddings.ApiKey ??= config.ApiKey;
            if (embeddings.Flavor == OpenAiFlavor.Auto) embeddings.Flavor = config.Flavor;
        }

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

        OpenAiCompatibleProvider Build(IServiceProvider sp) => new(
            id,
            config,
            resolveClient(sp),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<OpenAiCompatibleProvider>>(),
            disposeHttpClient: !byo); // dispose only Lyntai-created clients

        // A host serving both routes is ONE backend, and both front doors must see it. AddEmbeddingProvider
        // also arms the routed IEmbedder (D129), which AddProvider alone does not.
        if (config.Embeddings is null) builder.AddProvider(Build);
        else builder.AddEmbeddingProvider(Build);
        return builder;
    }

    // ---- pre-configured presets ------------------------------------------------------------------
    // Thin wrappers over AddOpenAiCompatibleProvider with sensible defaults for common endpoints. Apps
    // that need something bespoke keep using AddOpenAiCompatibleProvider (or their own IModelProvider via
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

    /// <summary>A local (or remote) Ollama endpoint, pinned to Ollama's NATIVE surface
    /// (<see cref="OpenAiFlavor.Ollama"/>). Default base "http://localhost:11434", id "ollama".
    /// <para><paramref name="baseUrl"/> must be the server ROOT (e.g. <c>http://localhost:11434</c>), never
    /// its <c>/v1</c> OpenAI-compatible surface: the pin is applied over whatever URL you pass, so a
    /// <c>/v1</c> base composes to <c>…/v1/api/chat</c> and 404s on the first call. Send a <c>/v1</c> base
    /// through <see cref="AddOpenAiCompatibleProvider"/> instead, whose detection resolves it correctly.</para>
    /// <para>Attachments are carried: an <see cref="Lyntai.Llm.LlmAttachment"/> with <c>Data</c> travels in
    /// Ollama's own <c>images</c> array on a user turn (pair it with a vision model — <c>llava</c> and
    /// friends). An attachment carrying only a remote <c>Uri</c> is the one shape this endpoint cannot take,
    /// since <c>/api/chat</c> has no URL form; it is logged as undeliverable rather than dropped in
    /// silence.</para></summary>
    public static LyntaiBuilder AddOllamaProvider(this LyntaiBuilder builder, string? baseUrl = null,
        string? defaultModel = null, string id = "ollama", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiCompatibleProvider(id, o =>
        {
            o.BaseUrl = baseUrl ?? "http://localhost:11434";
            o.DefaultModel = defaultModel;
            o.Flavor = OpenAiFlavor.Ollama;
        }, httpClient);

    /// <summary>A local (or remote) llama.cpp <c>llama-server</c>, which speaks the plain OpenAI schema off
    /// its ROOT (<see cref="OpenAiFlavor.OpenAi"/>). Default base "http://localhost:8080", id "llama",
    /// keyless.
    /// <para><b>What <paramref name="defaultModel"/> means here is not what it means on a catalogue
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
        string? defaultModel = null, string id = "llama", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiCompatibleProvider(id, o =>
        {
            o.BaseUrl = baseUrl ?? "http://localhost:8080";
            o.DefaultModel = defaultModel;
            o.Flavor = OpenAiFlavor.OpenAi;
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

        // A PROVIDER declaring ProviderOperation.Embed rather than the single embedder slot (D129): the
        // IEmbedder a consumer resolves is the routing front door over every such backend, so registering
        // two endpoints now gives fallback instead of the second silently winning.
        builder.AddEmbeddingProvider(sp => new HttpEmbedder(
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
