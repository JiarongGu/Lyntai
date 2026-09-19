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

    /// <param name="id">The router-facing provider id.</param>
    /// <param name="config">The registration's options; see <see cref="OllamaOptions"/>.</param>
    /// <param name="httpFactory">Per-call client source; see <c>AddOllamaProvider</c> for the BYO story.</param>
    /// <param name="options">Timeout configuration.</param>
    /// <param name="logger">Optional diagnostics — also where an undeliverable attachment is reported.</param>
    /// <param name="disposeHttpClient">True for Lyntai-created clients (disposed per call); false for an
    /// APP-supplied client, whose lifetime the app owns.</param>
    /// <exception cref="NotSupportedException"><paramref name="config"/> declares
    /// <see cref="ProviderKinds.Score"/> — Ollama serves no rerank surface, and refusing here is a
    /// composition error heard while a human is watching rather than a 404 on the first call.</exception>
    public OllamaProvider(
        string id,
        OllamaOptions config,
        Func<HttpClient> httpFactory,
        LyntaiOptions options,
        ILogger<OllamaProvider>? logger = null,
        bool disposeHttpClient = true)
    {
        _id = id;
        _config = config;
        Capabilities = CapabilitiesFor(config);
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
    /// <exception cref="NotSupportedException">The options declare <see cref="ProviderKinds.Score"/>; see
    /// the constructor's remarks.</exception>
    internal static ProviderCapabilities CapabilitiesFor(OllamaOptions config)
    {
        if (Serves(config, ProviderKinds.Score))
            throw new NotSupportedException(
                "Ollama serves no rerank surface, so Produces = ProviderKinds.Score cannot be registered "
                + "against it. Register an OpenAI-shaped reranker (llama-server --reranking, TEI, vLLM) "
                + "with AddHttpProvider instead.");
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
        QueryPrefix: c.QueryPrefix);

    public bool IsAvailable => !string.IsNullOrWhiteSpace(_config.BaseUrl);

    /// <inheritdoc/>
    public Task<TextResponse> CompleteAsync(TextRequest req, CancellationToken ct = default) =>
        Chat().CompleteAsync(req, ct);

    /// <inheritdoc/>
    /// <remarks>NDJSON: one JSON object per line, terminated by <c>done:true</c>, whose line also carries
    /// the eval counts that become <see cref="TextChunk.Usage"/> on the Final chunk. Tool calls arrive
    /// complete on one line and are still delivered before the terminal chunk, like every provider.</remarks>
    public IAsyncEnumerable<TextChunk> StreamAsync(TextRequest req, CancellationToken ct = default) =>
        Chat().StreamAsync(req, ct);

    /// <inheritdoc/>
    /// <exception cref="NotSupportedException">This registration does not produce vectors. A router checks
    /// <see cref="Capabilities"/> first, so only a caller that ignored them reaches this.</exception>
    public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default) =>
        (_embed ?? throw new NotSupportedException(
            $"{_id} produces {_config.Produces}, not {ProviderKinds.Vector} — an embedding model is its own "
            + "backend, registered with Produces = ProviderKinds.Vector."))
        .CallAsync(request, ct);

    private HttpChatEngine Chat() => _chat
        ?? throw new NotSupportedException(
            $"{_id} produces {_config.Produces}, not {ProviderKinds.Text} — a chat model is its own "
            + "backend, registered with Produces = ProviderKinds.Text.");
}
