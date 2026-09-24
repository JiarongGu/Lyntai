using Lyntai.Inference;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Providers.Http;

/// <summary>A model served over HTTP in the OpenAI-shaped schema — <c>chat/completions</c>,
/// <c>embeddings</c> or <c>rerank</c> under the <c>/v1</c> convention, which is what OpenAI, OpenRouter,
/// llama.cpp's <c>llama-server</c>, LM Studio, vLLM and Azure's v1 surface all speak.
/// <see cref="HttpModelOptions.Produces"/> says which of the three routes THIS registration serves.
///
/// <para><b>It is named for its transport because the backend is any host speaking the schema</b> — naming
/// it for one vendor was retired by <c>docs/DECISIONS.md</c> D135/D158. A backend with its OWN wire schema
/// is its own provider class: Ollama-native is <see cref="Lyntai.Providers.Ollama.OllamaProvider"/>
/// (<b>D160</b>), exactly as each CLI backend is its own provider over the one
/// <see cref="Lyntai.Inference.Cli.CliProviderEngine"/>.</para>
///
/// <para>Maps HTTP status → verdict (429 RateLimited, 5xx Failed, deadline Timeout, content-filter
/// Refused) and retries once on a malformed body (design §6) — the shared rules live in the internal
/// engine both HTTP chat providers compose.</para></summary>
public sealed class HttpModelProvider : IModelProvider, IVectorProvider, IScoreProvider
{
    private readonly string _id;
    private readonly HttpModelOptions _config;
    private readonly HttpChatEngine? _chat;

    /// <param name="id">The router-facing provider id.</param>
    /// <param name="config">The registration's options; see <see cref="HttpModelOptions"/>.</param>
    /// <param name="httpFactory">Per-call client source; see <c>AddHttpProvider</c> for the BYO story.</param>
    /// <param name="options">Timeout configuration.</param>
    /// <param name="logger">Optional diagnostics.</param>
    /// <param name="disposeHttpClient">True for Lyntai-created clients (disposed per call); false for an
    /// APP-supplied client, whose lifetime the app owns.</param>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="HttpModelOptions.MaxInputChars"/> is not
    /// positive, or leaves an embedding prefix no room for text.</exception>
    public HttpModelProvider(
        string id,
        HttpModelOptions config,
        Func<HttpClient> httpFactory,
        LyntaiOptions options,
        ILogger<HttpModelProvider>? logger = null,
        bool disposeHttpClient = true)
    {
        ValidateInputBound(config);
        _id = id;
        _config = config;
        Capabilities = CapabilitiesFor(config);
        ILogger log = logger ?? NullLogger<HttpModelProvider>.Instance;
        _chat = ServesText(config)
            ? new HttpChatEngine(id, new OpenAiChatWire(config), httpFactory, options, log, disposeHttpClient)
            : null;
        _embeddings = ServesVectors(config)
            ? new HttpVectorTransport(id, VectorSettings(config), httpFactory, options, log, disposeHttpClient)
            : null;
        _rerank = ServesScores(config)
            ? new HttpRerankTransport(id, config, httpFactory, options, log, disposeHttpClient)
            : null;
    }

    public string Id => _id;

    /// <summary>What this backend serves, DERIVED from <see cref="HttpModelOptions.Produces"/>: text is
    /// buffered or streamed with native tool calls on both paths; vector (<c>embeddings</c>) and score
    /// (<c>rerank</c>) are one batched call each, since there is no such thing as a partially delivered
    /// embedding or a partial ranking.
    /// <para>Models are NOT enumerated — an aggregator fronts hundreds behind one id, which is exactly the
    /// case an empty Models list means "any" for.</para></summary>
    public ProviderCapabilities Capabilities { get; }

    /// <summary>The capabilities a registration with these options serves — the SAME derivation the built
    /// provider carries, exposed so the registration's composition-time declaration cannot drift from the
    /// built truth (they were two copies once, and the declared one silently dropped Stream).</summary>
    internal static ProviderCapabilities CapabilitiesFor(HttpModelOptions config) => new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [config.Produces],
        Operations = ServesText(config)
            ? [ProviderOperation.Complete, ProviderOperation.Stream]
            : [ProviderOperation.Complete],
        SupportsToolCalls = ServesText(config),
        SupportsStreamingToolCalls = ServesText(config),
    };

    /// <summary>Which kind this registration serves — one field decides the route, the wire shape, and which
    /// methods answer.</summary>
    private static bool Serves(HttpModelOptions c, string kind) =>
        string.Equals(c.Produces, kind, StringComparison.OrdinalIgnoreCase);

    private static bool ServesText(HttpModelOptions c) => Serves(c, ProviderKinds.Text);
    private static bool ServesVectors(HttpModelOptions c) => Serves(c, ProviderKinds.Vector);
    private static bool ServesScores(HttpModelOptions c) => Serves(c, ProviderKinds.Score);

    private static HttpVectorTransport.Settings VectorSettings(HttpModelOptions c) => new(
        Endpoint: HttpEndpoint.Build(c.BaseUrl, HttpEndpoint.AzureFor(c), "embeddings"),
        ApiKey: c.ApiKey,
        AzureConventions: HttpEndpoint.AzureFor(c),
        Model: c.Model,
        BatchSize: c.BatchSize,
        DocumentPrefix: c.DocumentPrefix,
        QueryPrefix: c.QueryPrefix,
        MaxInputChars: c.MaxInputChars,
        Segmentation: c.Segmentation);

    /// <summary>Throws when <see cref="HttpModelOptions.MaxInputChars"/> cannot bound a piece — run at
    /// registration as well as here, so a bad bound fails composition rather than a first call.</summary>
    internal static void ValidateInputBound(HttpModelOptions c)
    {
        if (ServesVectors(c) || ServesScores(c))
            InputSegmenter.ValidateBound(c.MaxInputChars, ServesVectors(c), c.DocumentPrefix, c.QueryPrefix);
    }

    /// <summary>The <c>/embeddings</c> wire shape, or null when this backend produces something else. It is
    /// composed rather than inherited: an embeddings call has nothing in common with a completion beyond the
    /// host it is posted to.</summary>
    private readonly HttpVectorTransport? _embeddings;

    /// <summary>The <c>/v1/rerank</c> wire shape, or null when this backend produces something else.</summary>
    private readonly HttpRerankTransport? _rerank;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.BaseUrl);

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">This registration does not produce vectors. A router checks
    /// <see cref="Capabilities"/> first, so only a caller that ignored them reaches this.</exception>
    public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default) =>
        (_embeddings ?? throw new NotSupportedException(
            $"{_id} produces {_config.Produces}, not {ProviderKinds.Vector} — an embedding model is its own "
            + "backend, registered with Produces = ProviderKinds.Vector."))
        .CallAsync(request, ct);

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">This registration does not produce scores. A router checks
    /// <see cref="Capabilities"/> first, so only a caller that ignored them reaches this.</exception>
    public Task<ScoreResponse> CallAsync(ScoreRequest request, CancellationToken ct = default) =>
        (_rerank ?? throw new NotSupportedException(
            $"{_id} produces {_config.Produces}, not {ProviderKinds.Score} — a reranker is its own backend, "
            + "registered with Produces = ProviderKinds.Score."))
        .CallAsync(request, ct);

    /// <inheritdoc/>
    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
        Chat().CompleteAsync(req, ct);

    /// <inheritdoc/>
    /// <remarks>True tool-call streaming since 3.0: the stream assembles the vendor's fragmented tool-call
    /// deltas and yields complete calls as <see cref="TextChunkKind.ToolCall"/> chunks.</remarks>
    public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        Chat().StreamAsync(req, ct);

    private HttpChatEngine Chat() => _chat
        ?? throw new NotSupportedException(
            $"{_id} produces {_config.Produces}, not {ProviderKinds.Text} — a chat model is its own "
            + "backend, registered with Produces = ProviderKinds.Text.");
}
