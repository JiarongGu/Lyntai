using Lyntai.Inference;
using Lyntai.Providers.Http;
using Lyntai.Providers.Ollama;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddHttpProvider` shows up right on the builder.
namespace Lyntai;

/// <summary>DI entry points for models served over HTTP in the OpenAI-shaped schema
/// (<see cref="HttpModelProvider"/>), plus the vendor presets that pre-configure it.</summary>
public static class HttpProviderBuilderExtensions
{
    /// <summary>Register a model served over HTTP under <paramref name="id"/> (also usable multiple times
    /// with different ids — e.g. a chat and an embedding backend on one server, registered twice).
    /// <para><b>The wire is the OpenAI-shaped schema</b> — with one composition-time exception kept for the
    /// URL every local-model reader pastes: a BaseUrl that is an Ollama server ROOT (its well-known port,
    /// no <c>/v1</c>) composes the Ollama-NATIVE provider instead, exactly as <c>AddOllamaProvider</c>
    /// would. An Ollama <c>/v1</c> base stays here, on the OpenAI-shaped wire its path names.</para>
    /// <para><b>This one KEEPS the <c>Provider</c> suffix</b> where a named backend drops it (<b>D134</b>):
    /// like <see cref="LyntaiBuilder.AddProvider(Func{IServiceProvider,Lyntai.Inference.IModelProvider},Lyntai.Inference.ProviderCapabilities)"/>
    /// it is the GENERIC registration, so <c>Provider</c> is the noun it takes rather than a suffix on a
    /// vendor's name. The vendor presets below — <see cref="AddOpenAiProvider"/>,
    /// <see cref="OllamaBuilderExtensions.AddOllamaProvider(LyntaiBuilder,string?,string?,string,Func{IServiceProvider,HttpClient}?)"/>
    /// — name a backend, so they do not carry it.</para>
    /// <para>BYO HttpClient: pass <paramref name="httpClient"/> to supply your own configured client
    /// (Polly resilience, auth handlers, a proxy, service discovery, or an existing named
    /// <see cref="IHttpClientFactory"/> client — e.g. <c>sp =&gt; sp.GetRequiredService&lt;IHttpClientFactory&gt;().CreateClient("my")</c>).
    /// You then own its timeout/lifecycle. When null (default), Lyntai registers a named client with an
    /// infinite HttpClient timeout so the per-call <see cref="LyntaiOptions.ProviderTimeout"/> owns deadlines.</para></summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="HttpModelOptions.MaxInputChars"/> is not
    /// positive, or leaves an embedding prefix no room for text.</exception>
    public static LyntaiBuilder AddHttpProvider(this LyntaiBuilder builder, string id,
        Action<HttpModelOptions> configure, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        var config = new HttpModelOptions();
        configure(config);

        // An Ollama ROOT speaks Ollama's own wire, whatever method it arrived through — one registration
        // site per backend, so this door and AddOllamaProvider cannot drift. Score stays here: rerank is
        // OpenAI-shaped only, and the Ollama provider refuses it at construction.
        if (!string.Equals(config.Produces, ProviderKinds.Score, StringComparison.OrdinalIgnoreCase)
            && ProviderDetect.IsOllamaRoot(config.BaseUrl))
        {
            return builder.AddOllamaProvider(id, new Providers.Ollama.OllamaOptions
            {
                BaseUrl = config.BaseUrl,
                ApiKey = config.ApiKey,
                Model = config.Model,
                Produces = config.Produces,
                BatchSize = config.BatchSize,
                DocumentPrefix = config.DocumentPrefix,
                QueryPrefix = config.QueryPrefix,
                MaxInputChars = config.MaxInputChars,
                Segmentation = config.Segmentation,
            }, httpClient);
        }

        return builder.AddOpenAiShaped(id, config, httpClient);
    }

    /// <summary>The OpenAI-shaped registration itself, with no composition-time URL detection — what the
    /// vendor presets call, so a preset's wire is decided by the PRESET rather than re-guessed from its
    /// URL (a llama-server that happens to sit on Ollama's port must stay OpenAI-shaped).</summary>
    private static LyntaiBuilder AddOpenAiShaped(this LyntaiBuilder builder, string id,
        HttpModelOptions config, Func<IServiceProvider, HttpClient>? httpClient)
    {
        HttpModelProvider.ValidateInputBound(config);
        var resolveClient = ResolveClient(builder, id, httpClient);

        HttpModelProvider Build(IServiceProvider sp) => new(
            id,
            config,
            resolveClient.Factory(sp),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<HttpModelProvider>>(),
            disposeHttpClient: resolveClient.LyntaiOwned);

        // ONE registration whatever this produces (D152). The declaration is derived by the same code the
        // built provider runs (CapabilitiesFor), because `Build` is a FACTORY: composition asks what is
        // registered before any provider exists to be asked, and a hand-restated copy silently dropped
        // Stream once.
        builder.AddProvider(Build, HttpModelProvider.CapabilitiesFor(config));
        return builder;
    }

    /// <summary>Resolve the per-call client source for one registration: the BYO factory when the app
    /// supplied one (never disposed by Lyntai), otherwise a named <see cref="IHttpClientFactory"/> client
    /// with an infinite timeout so the per-call <see cref="LyntaiOptions.ProviderTimeout"/> owns deadlines.
    /// Shared by every HTTP-backed registration in this package.</summary>
    internal static (Func<IServiceProvider, Func<HttpClient>> Factory, bool LyntaiOwned) ResolveClient(
        LyntaiBuilder builder, string id, Func<IServiceProvider, HttpClient>? httpClient)
    {
        if (httpClient is not null)
            return (sp => () => httpClient(sp), LyntaiOwned: false); // app-owned client + lifecycle

        builder.Services.AddHttpClient(HttpClientName(id))
            .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);
        return (sp => () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName(id)),
            LyntaiOwned: true);
    }

    // ---- pre-configured presets ------------------------------------------------------------------
    // Thin wrappers with sensible defaults for common endpoints. Apps that need something bespoke keep
    // AddHttpProvider (or their own IModelProvider via builder.AddProvider). All presets accept a BYO
    // httpClient like the base method. The Ollama preset lives with its provider: OllamaBuilderExtensions.

    /// <summary>OpenAI (api.openai.com). Default id "openai".</summary>
    public static LyntaiBuilder AddOpenAiProvider(this LyntaiBuilder builder, string apiKey,
        string? model = null, string id = "openai", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiShaped(id, new HttpModelOptions
        {
            BaseUrl = "https://api.openai.com",
            ApiKey = apiKey,
            Model = model,
        }, httpClient);

    /// <summary>A local (or remote) llama.cpp <c>llama-server</c>, which speaks the plain OpenAI schema off
    /// its ROOT. Default base "http://localhost:8080", id "llama", keyless.
    /// <para><b>What <paramref name="model"/> means here is not what it means on a catalogue
    /// endpoint.</b> A <c>llama-server</c> started with <c>--model</c> serves exactly ONE model and answers
    /// to whatever <c>--alias</c> names it, so the model on a request is a LABEL and a wrong one is not an
    /// error — you get the loaded model either way. It selects only on a router server
    /// (<c>--models-dir</c>), where the name must match an entry. Pass <see langword="null"/> unless you run
    /// a router or want the label recorded on traces.</para>
    /// <para>Pass the server ROOT, not its <c>/v1</c>: requests compose to <c>…/v1/chat/completions</c>.
    /// Unlike the Ollama preset there is no native surface to reach — <c>llama-server</c> has only the
    /// OpenAI-shaped one — so an attachment travels as an <c>image_url</c> part and a remote <c>Uri</c>
    /// attachment is deliverable, which Ollama's own schema cannot express.</para></summary>
    public static LyntaiBuilder AddLlamaProvider(this LyntaiBuilder builder, string? baseUrl = null,
        string? model = null, string id = "llama", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiShaped(id, new HttpModelOptions
        {
            BaseUrl = baseUrl ?? "http://localhost:8080",
            ApiKey = null,
            Model = model,
        }, httpClient);

    /// <summary>OpenRouter (openrouter.ai). Default id "openrouter". Nothing about its wire differs from
    /// the plain OpenAI schema today; the preset earns its keep as the endpoint + key convention.</summary>
    public static LyntaiBuilder AddOpenRouterProvider(this LyntaiBuilder builder, string apiKey,
        string? model = null, string id = "openrouter", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiShaped(id, new HttpModelOptions
        {
            BaseUrl = "https://openrouter.ai/api/v1",
            ApiKey = apiKey,
            Model = model,
        }, httpClient);

    /// <summary>Azure OpenAI, targeting the resource's OpenAI-shaped <c>v1</c> surface.
    /// <paramref name="endpoint"/> is your resource URL (e.g. <c>https://my-resource.openai.azure.com</c> —
    /// requests compose to <c>…/openai/v1/chat/completions</c>); <paramref name="apiKey"/> is sent as both
    /// the <c>api-key</c> header (Azure key auth) and a Bearer token. Default id "azure-openai". The preset
    /// pins <see cref="HttpModelOptions.AzureConventions"/>, so a custom domain fronting the resource works
    /// without the URL looking like Azure.</summary>
    public static LyntaiBuilder AddAzureOpenAiProvider(this LyntaiBuilder builder, string endpoint, string apiKey,
        string? model = null, string id = "azure-openai", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOpenAiShaped(id, new HttpModelOptions
        {
            BaseUrl = endpoint,
            ApiKey = apiKey,
            Model = model,
            AzureConventions = true,
        }, httpClient);

    // One name, because there is one registration per host now — an embeddings-only host is a provider with
    // Chat nulled, so it takes the provider client like any other (D132).
    internal static string HttpClientName(string id) => $"lyntai.provider.{id}";
}
