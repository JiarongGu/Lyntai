using Lyntai.Inference;
using Lyntai.Providers.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Ollama;

/// <summary>An Ollama server on its NATIVE surface — <c>/api/chat</c> + <c>/api/embed</c>, NDJSON
/// streaming, <c>options.num_ctx</c>, images as inline base64. Ollama's separate OpenAI-shaped <c>/v1</c>
/// surface is a different wire and a different provider: pass a <c>/v1</c> base to <c>AddHttpProvider</c>.
///
/// <para><b>Its own provider class because its wire is its own</b> (<c>docs/DECISIONS.md</c> <b>D160</b>) —
/// the same rule that gives each CLI backend and each media backend its own class. The rules every HTTP
/// chat backend shares (status→verdict, the retry-once, the inactivity clock, exactly one terminal chunk)
/// live once in the internal engine both this class and <see cref="HttpModelProvider"/> compose.</para>
///
/// <para>An attachment carrying only a remote <c>Uri</c> is the one shape the native wire cannot take
/// (<c>/api/chat</c> has no URL form, and Lyntai will not fetch bytes on your behalf) — it is REPORTED
/// through the logger rather than sent.</para></summary>
public sealed class OllamaProvider : IModelProvider, IVectorProvider
{
    private readonly string _id;
    private readonly OllamaOptions _config;
    private readonly HttpChatEngine? _chat;
    private readonly HttpVectorTransport? _embed;
    private readonly Func<HttpClient> _httpFactory;
    private readonly bool _disposeHttpClient;
    private readonly TimeSpan _timeout;

    /// <param name="id">The router-facing provider id.</param>
    /// <param name="config">The registration's options; see <see cref="OllamaOptions"/>.</param>
    /// <param name="httpFactory">Per-call client source; see <c>AddOllamaProvider</c> for the BYO story.</param>
    /// <param name="options">Timeout configuration.</param>
    /// <param name="logger">Optional diagnostics — also where an undeliverable attachment is reported.</param>
    /// <param name="disposeHttpClient">True for Lyntai-created clients (disposed per call); false for an
    /// APP-supplied client, whose lifetime the app owns.</param>
    /// <exception cref="ArgumentException"><paramref name="config"/> declares a kind other than
    /// <see cref="ProviderKinds.Text"/> or <see cref="ProviderKinds.Vector"/> — notably
    /// <see cref="ProviderKinds.Score"/>, since Ollama serves no rerank surface. Refusing here is a composition
    /// error heard while a human is watching rather than a 404 on the first call.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="OllamaOptions.MaxInputChars"/> is not
    /// positive, or leaves an embedding prefix no room for text.</exception>
    public OllamaProvider(
        string id,
        OllamaOptions config,
        Func<HttpClient> httpFactory,
        LyntaiOptions options,
        ILogger<OllamaProvider>? logger = null,
        bool disposeHttpClient = true)
    {
        Capabilities = CapabilitiesFor(config);
        ValidateInputBound(config);
        _id = id;
        _config = config;
        _httpFactory = httpFactory;
        _disposeHttpClient = disposeHttpClient;
        _timeout = options.ProviderTimeout;
        ILogger log = logger ?? NullLogger<OllamaProvider>.Instance;
        _chat = ServesText(config)
            ? new HttpChatEngine(id, new OllamaChatWire(config, log), httpFactory, options, log, disposeHttpClient)
            : null;
        _embed = ServesVectors(config)
            ? new HttpVectorTransport(id, EmbedSettings(config), httpFactory, options, log, disposeHttpClient)
            : null;
    }

    public string Id => _id;

    /// <summary>What this backend serves, DERIVED from <see cref="OllamaOptions.Produces"/>: text is
    /// buffered or streamed with native tool calls on both paths; vector is one batched <c>/api/embed</c>
    /// call, since there is no such thing as a partially delivered embedding.
    /// <para>Models are NOT enumerated — one server fronts whatever is pulled, which is exactly the case an
    /// empty Models list means "any" for.</para></summary>
    public ProviderCapabilities Capabilities { get; }

    /// <summary>The capabilities a registration with these options serves — the SAME derivation the built
    /// provider carries, exposed so the registration's composition-time declaration cannot drift from the
    /// built truth.</summary>
    /// <exception cref="ArgumentException">The options declare a kind this wire does not serve; see the
    /// constructor.</exception>
    internal static ProviderCapabilities CapabilitiesFor(OllamaOptions config)
    {
        if (!ServesText(config) && !ServesVectors(config))
            throw new ArgumentException(
                $"{nameof(OllamaOptions)}.{nameof(OllamaOptions.Produces)} is '{config.Produces}', which this "
                + $"backend does not serve. Ollama's native surface produces {ProviderKinds.Text} (/api/chat) "
                + $"or {ProviderKinds.Vector} (/api/embed); it has no rerank surface, so register an OpenAI-shaped "
                + "reranker (llama-server --reranking, TEI, vLLM) with AddHttpProvider instead.",
                nameof(config));
        return new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [config.Produces],
            Operations = ServesText(config)
                ? [ProviderOperation.Complete, ProviderOperation.Stream]
                : [ProviderOperation.Complete],
            SupportsToolCalls = ServesText(config),
            SupportsStreamingToolCalls = ServesText(config),
        };
    }

    private static bool Serves(OllamaOptions c, string kind) =>
        string.Equals(c.Produces, kind, StringComparison.OrdinalIgnoreCase);

    private static bool ServesText(OllamaOptions c) => Serves(c, ProviderKinds.Text);
    private static bool ServesVectors(OllamaOptions c) => Serves(c, ProviderKinds.Vector);

    private static HttpVectorTransport.Settings EmbedSettings(OllamaOptions c) => new(
        Endpoint: new Uri(c.BaseUrl.TrimEnd('/') + "/api/embed"),
        ApiKey: c.ApiKey,
        AzureConventions: false,
        Model: c.Model,
        BatchSize: c.BatchSize,
        DocumentPrefix: c.DocumentPrefix,
        QueryPrefix: c.QueryPrefix,
        MaxInputChars: c.MaxInputChars,
        Segmentation: c.Segmentation,
        // under Truncate the deployment accepted loss, so the server's own cut may stand behind the client's
        NoServerTruncation: c.MaxInputChars is not null && c.Segmentation?.Overflow != InputOverflow.Truncate);

    /// <summary>Throws when <see cref="OllamaOptions.MaxInputChars"/> cannot bound a piece — run at
    /// registration as well as here, so a bad bound fails composition rather than a first call.</summary>
    internal static void ValidateInputBound(OllamaOptions c)
    {
        if (ServesVectors(c))
            InputSegmenter.ValidateBound(c.MaxInputChars, embeds: true, c.DocumentPrefix, c.QueryPrefix);
    }

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.BaseUrl);

    /// <summary>Asks the server: one GET of <c>/api/tags</c>, which generates nothing. Unavailable when it is
    /// unreachable, refuses the key or errors. <see cref="ProviderProbeResult.Models"/> carries the pulled models,
    /// and <see cref="ProviderProbeResult.Model"/> the configured one when it is pulled — an untagged name matches
    /// its <c>:latest</c>, as Ollama resolves it.</summary>
    public Task<ProviderProbeResult> ProbeAsync(CancellationToken ct = default)
    {
        if (!IsAvailable) return Task.FromResult(new ProviderProbeResult(false, "not configured: no BaseUrl"));
        return HttpProbe.RunAsync(_httpFactory, _disposeHttpClient, _timeout,
            new Uri(_config.BaseUrl.TrimEnd('/') + "/api/tags"),
            request => HttpEndpoint.ApplyAuth(request, _config.ApiKey, azureConventions: false),
            HttpEndpoint.HasCredentials(_config.ApiKey), _config.Model,
            root => root.GetProperty("models").EnumerateArray().Select(m => m.GetProperty("name").GetString()).OfType<string>(),
            Pulled, ct);
    }

    /// <summary>Whether a pulled name is the configured model: exact, or its <c>:latest</c> when the configured
    /// name carries no tag.</summary>
    private static bool Pulled(string listed, string configured) =>
        string.Equals(listed, configured, StringComparison.OrdinalIgnoreCase)
        || (!configured.Contains(':') && string.Equals(listed, configured + ":latest", StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    /// <remarks>A registration that does not produce text answers <see cref="ProviderVerdict.Unsupported"/>.</remarks>
    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
        _chat?.CompleteAsync(req, ct) ?? Task.FromResult(new TextResponse("", ProviderVerdict.Unsupported,
            Detail: WrongKindCall.Detail(_id, _config.Produces, ProviderKinds.Text)));

    /// <inheritdoc/>
    /// <remarks>NDJSON: one JSON object per line, terminated by <c>done:true</c>, whose line also carries
    /// the eval counts that become <see cref="TextChunk.Usage"/> on the Final chunk. Each tool call arrives
    /// complete on its own line and is delivered before the terminal chunk, like every provider. A
    /// registration that does not produce text answers one <see cref="ProviderVerdict.Unsupported"/> error
    /// chunk.</remarks>
    public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        _chat?.StreamAsync(req, ct)
        ?? WrongKindCall.Stream(WrongKindCall.Detail(_id, _config.Produces, ProviderKinds.Text));

    /// <inheritdoc/>
    /// <remarks>A registration that does not produce vectors answers <see cref="ProviderVerdict.Unsupported"/>
    /// — a router checks <see cref="Capabilities"/> first, so only a direct caller sees it.</remarks>
    public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default) =>
        _embed?.CallAsync(request, ct) ?? Task.FromResult(VectorResponse.Failure(
            ProviderVerdict.Unsupported, WrongKindCall.Detail(_id, _config.Produces, ProviderKinds.Vector)));
}
