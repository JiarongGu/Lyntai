using Lyntai.Providers.Ollama;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddOllamaProvider` shows up right on the builder.
namespace Lyntai;

/// <summary>DI entry points for the Ollama-native backend (<see cref="OllamaProvider"/>). A consumer
/// composes it through the builder and never constructs its types by hand.</summary>
public static class OllamaBuilderExtensions
{
    /// <summary>A local (or remote) Ollama endpoint on its NATIVE surface. Default base
    /// "http://localhost:11434", id "ollama".
    /// <para><paramref name="baseUrl"/> must be the server ROOT (e.g. <c>http://localhost:11434</c>), never
    /// its <c>/v1</c> OpenAI-shaped surface — that surface is a different wire, served by
    /// <c>AddHttpProvider</c>, and the native <c>/api/chat</c> composed over a <c>/v1</c> base would 404 on
    /// the first call.</para>
    /// <para>Attachments are carried: a <see cref="Lyntai.Inference.TextAttachment"/> with <c>Data</c>
    /// travels in Ollama's own <c>images</c> array on a user turn (pair it with a vision model —
    /// <c>llava</c> and friends). An attachment carrying only a remote <c>Uri</c> is the one shape this
    /// endpoint cannot take, since <c>/api/chat</c> has no URL form; it is logged as undeliverable rather
    /// than dropped in silence.</para></summary>
    public static LyntaiBuilder AddOllamaProvider(this LyntaiBuilder builder, string? baseUrl = null,
        string? model = null, string id = "ollama", Func<IServiceProvider, HttpClient>? httpClient = null) =>
        builder.AddOllamaProvider(id, o =>
        {
            o.BaseUrl = baseUrl ?? "http://localhost:11434";
            o.Model = model;
        }, httpClient);

    /// <summary>The options door — an embedding registration (<c>o.Produces = ProviderKinds.Vector</c>, one
    /// batched <c>/api/embed</c> call), a context-window override (<c>o.ContextSize</c> →
    /// <c>options.num_ctx</c>), a proxied server's key. BYO HttpClient exactly as
    /// <c>AddHttpProvider</c> takes one: pass <paramref name="httpClient"/> and own its lifetime; when null,
    /// Lyntai registers a named client with an infinite timeout so the per-call
    /// <see cref="LyntaiOptions.ProviderTimeout"/> owns deadlines.</summary>
    /// <exception cref="NotSupportedException"><c>o.Produces</c> is <c>ProviderKinds.Score</c> — Ollama
    /// serves no rerank surface; register an OpenAI-shaped reranker with <c>AddHttpProvider</c>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><c>o.MaxInputChars</c> is not positive, or leaves an
    /// embedding prefix no room for text.</exception>
    public static LyntaiBuilder AddOllamaProvider(this LyntaiBuilder builder, string id,
        Action<OllamaOptions> configure, Func<IServiceProvider, HttpClient>? httpClient = null)
    {
        var config = new OllamaOptions();
        configure(config);
        return builder.AddOllamaProvider(id, config, httpClient);
    }

    /// <summary>The one registration site — also reached by <c>AddHttpProvider</c> when its BaseUrl is an
    /// Ollama server root, so the two doors cannot drift.</summary>
    internal static LyntaiBuilder AddOllamaProvider(this LyntaiBuilder builder, string id,
        OllamaOptions config, Func<IServiceProvider, HttpClient>? httpClient)
    {
        // throws on Produces = Score, or a bound that cannot hold a piece, BEFORE anything registers
        var declared = OllamaProvider.CapabilitiesFor(config);
        OllamaProvider.ValidateInputBound(config);
        var resolveClient = HttpProviderBuilderExtensions.ResolveClient(builder, id, httpClient);

        OllamaProvider Build(IServiceProvider sp) => new(
            id,
            config,
            resolveClient.Factory(sp),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<OllamaProvider>>(),
            disposeHttpClient: resolveClient.LyntaiOwned);

        builder.AddProvider(Build, declared);
        return builder;
    }
}
