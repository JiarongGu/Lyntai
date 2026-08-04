using Lyntai.Generation;
using Lyntai.Generation.Providers;
using Lyntai.Processes;
using Microsoft.Extensions.DependencyInjection;

// Lives in the Lyntai namespace so the Add* methods appear on the builder, exactly like the LLM-side presets.
namespace Lyntai;

/// <summary>One-line registration per media backend — the generation counterpart of <c>AddOpenAiProvider()</c> /
/// <c>AddOllamaProvider()</c>. Before these, every backend in this package had to be hand-constructed WITH its
/// <c>Func&lt;HttpClient&gt;</c>, which is a lot of ceremony to ask of someone whose LLM providers register in
/// one call each.
///
/// <para><b>Options are passed as an object, not an <c>Action&lt;T&gt;</c> configure callback</b> (the shape the
/// LLM presets use). These options are records with <c>required</c>/<c>init</c> members, so there is nothing to
/// mutate after construction — and passing the instance is what keeps <c>required BaseUrl</c>
/// compiler-enforced rather than discovered at the first render.</para>
///
/// <para><b>BYO HttpClient</b> stays optional on every method (design §7). Supply one to own its configuration
/// and lifecycle — a Polly-resilient client, an auth handler, a proxy, service discovery, or a named
/// <see cref="IHttpClientFactory"/> client (<c>sp =&gt;
/// sp.GetRequiredService&lt;IHttpClientFactory&gt;().CreateClient("my")</c>). Lyntai then never disposes it. When
/// omitted, Lyntai registers a named client with an INFINITE <see cref="HttpClient"/> timeout, because a render
/// legitimately runs for minutes and the 100-second default would abort a healthy one as a transport
/// failure.</para></summary>
public static class GenerationProviderBuilderExtensions
{
    /// <summary>An OpenAI-compatible images endpoint — the cloud service, or any local server speaking the same
    /// shape. Default id <c>"openai-images"</c> (<see cref="OpenAiImageOptions.Id"/>).</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">Endpoint, credential and defaults.</param>
    /// <param name="httpClient">BYO client — see the type summary. Null = Lyntai's own.</param>
    public static LyntaiBuilder AddOpenAiImageProvider(this LyntaiBuilder builder,
        OpenAiImageOptions options, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return builder.AddGenerationProvider(HttpBackend(builder, options.Id, httpClient,
            (client, dispose) => new OpenAiImageProvider(options, client, dispose)));
    }

    /// <summary>A locally-run Stable Diffusion WebUI (Automatic1111). Default id <c>"a1111"</c>
    /// (<see cref="Automatic1111Options.Id"/>).</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">Endpoint and sampling defaults.</param>
    /// <param name="httpClient">BYO client — see the type summary. Null = Lyntai's own.</param>
    public static LyntaiBuilder AddAutomatic1111Provider(this LyntaiBuilder builder,
        Automatic1111Options options, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return builder.AddGenerationProvider(HttpBackend(builder, options.Id, httpClient,
            (client, dispose) => new Automatic1111Provider(options, client, dispose)));
    }

    /// <summary>A ComfyUI server, driven by workflow graphs the HOST supplies. Default id <c>"comfyui"</c>
    /// (<see cref="ComfyUiOptions.Id"/>). Note this backend's surface is documented-not-measured — every
    /// endpoint path is an option for exactly that reason.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">Endpoint paths, declared kinds and option keys.</param>
    /// <param name="httpClient">BYO client — see the type summary. Null = Lyntai's own.</param>
    public static LyntaiBuilder AddComfyUiProvider(this LyntaiBuilder builder,
        ComfyUiOptions options, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return builder.AddGenerationProvider(HttpBackend(builder, options.Id, httpClient,
            (client, dispose) => new ComfyUiProvider(options, client, dispose)));
    }

    /// <summary>The fal.ai queue — submit/poll/fetch, which is the shape a video render needs. Default id
    /// <c>"fal"</c> (<see cref="FalQueueOptions.Id"/>). This backend's wire format is documented-not-measured
    /// (TASKS.md GEN-VERIFY); every URL segment is an option so a host can retarget it.</summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">Endpoint, credential and declared kinds.</param>
    /// <param name="httpClient">BYO client — see the type summary. Null = Lyntai's own.</param>
    public static LyntaiBuilder AddFalProvider(this LyntaiBuilder builder,
        FalQueueOptions options, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return builder.AddGenerationProvider(HttpBackend(builder, options.Id, httpClient,
            (client, dispose) => new FalQueueProvider(options, client, dispose)));
    }

    /// <summary>A locally-installed <c>stable-diffusion.cpp</c> (<c>sd-cli</c>) — image generation with no key,
    /// no network and no content policy in the path. Default id <c>"local-diffusion"</c>
    /// (<see cref="LocalDiffusionOptions.Id"/>).
    ///
    /// <para>This one spawns rather than calls, so its seam is <see cref="IProcessRunner"/> rather than
    /// <see cref="HttpClient"/> — taken from DI by default, which is what lets a host's sandboxed or audited
    /// runner apply here too. The engine and its weights are the host's to provide; Lyntai downloads
    /// neither.</para></summary>
    /// <param name="builder">The builder.</param>
    /// <param name="options">Engine paths and sampling defaults.</param>
    /// <param name="runner">BYO process runner. Null = the one registered in DI.</param>
    public static LyntaiBuilder AddLocalDiffusionProvider(this LyntaiBuilder builder,
        LocalDiffusionOptions options, Func<IServiceProvider, IProcessRunner>? runner = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        return builder.AddGenerationProvider(sp => new LocalDiffusionProvider(options,
            runner?.Invoke(sp) ?? sp.GetRequiredService<IProcessRunner>()));
    }

    /// <summary>The name of the <see cref="IHttpClientFactory"/> client Lyntai registers for a backend id.
    /// Exposed so a host can reach the SAME client the backend uses — to add a delegating handler, a Polly
    /// policy or a header — without replacing the wiring: <c>services.AddHttpClient(
    /// GenerationProviderBuilderExtensions.HttpClientName("fal")).AddHttpMessageHandler(...)</c>.</summary>
    /// <param name="id">The backend's candidate id.</param>
    public static string HttpClientName(string id) => $"lyntai.generation.{id}";

    /// <summary>The shared BYO-or-ours decision, in one place: a host-supplied client is never disposed by us
    /// (it outlives the call and is the host's to manage), while a client Lyntai created per call is. Getting
    /// this wrong is not a leak but an <see cref="ObjectDisposedException"/> on the SECOND render — the first
    /// one succeeds, which is what makes it worth centralising.</summary>
    private static Func<IServiceProvider, IGenerationProvider> HttpBackend(
        LyntaiBuilder builder, string id, Func<IServiceProvider, HttpClient>? httpClient,
        Func<Func<HttpClient>, bool, IGenerationProvider> create)
    {
        if (httpClient is not null)
            return sp => create(() => httpClient(sp), false);

        // the per-call deadline owns timeouts, not HttpClient's default 100s — a render outlives it routinely
        builder.Services.AddHttpClient(HttpClientName(id))
            .ConfigureHttpClient(c => c.Timeout = Timeout.InfiniteTimeSpan);

        return sp => create(
            () => sp.GetRequiredService<IHttpClientFactory>().CreateClient(HttpClientName(id)), true);
    }
}
