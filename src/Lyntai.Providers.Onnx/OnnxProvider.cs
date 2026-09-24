using Lyntai.Inference;
using Lyntai.Text;
using Microsoft.ML.OnnxRuntime;

namespace Lyntai.Providers.Onnx;

/// <summary>An <see cref="IModelProvider"/> running a TRANSFORMER in process through ONNX Runtime — no
/// HTTP endpoint, no server, no port.
///
/// <para><b>This class is the ENGINE, and it is pure</b> (<c>docs/DECISIONS.md</c> <b>D157</b>): it opens
/// a session, tokenizes, feeds and runs. It pools nothing and reads no logit. What a call means at each
/// end belongs to a HEAD chosen by <see cref="OnnxProviderOptions.Produces"/>, and that is what decides
/// what this provider PRODUCES — an embedding model and a reranker are the same backend with different
/// weights, so a kind is never a reason to fork this class.</para>
///
/// <para><b>What it buys over the static class, measured.</b> On tool routing a 25,008,064 B transformer
/// reads 78.6% at three options against a 30,236,760 B <c>model2vec</c> table's 69.0%
/// (<c>docs/memory-measurements.md</c> §5). What it costs is ~16 MB of native runtime and this package's
/// trim/AOT claim.</para>
///
/// <para><b>It HAS a context limit</b>, which the static class does not: a BERT encoder has positional
/// embeddings, so input past <see cref="OnnxProviderOptions.MaxTokens"/> is truncated — or, where
/// <see cref="OnnxProviderOptions.Segmentation"/> says so, segmented by tokens and every window answered
/// (<c>docs/DECISIONS.md</c> <b>D177</b>).</para>
///
/// <para><b>Inference runs on the calling thread.</b> The async signature is the seam's, not a promise to
/// yield — a call is CPU-bound for as long as its forward passes take, and a segmented input adds a pass
/// per window. Wrap the call if that matters to your scheduler.</para></summary>
public sealed class OnnxProvider : IVectorProvider, IScoreProvider, IDisposable
{
    private readonly InferenceSession _session;
    private readonly WindowedTokenizer _windows;
    private readonly IOnnxHead _head;
    private readonly string _outputName;

    private OnnxProvider(InferenceSession session, WindowedTokenizer windows, IOnnxHead head, string id)
    {
        _session = session;
        _windows = windows;
        _head = head;
        Id = id;
        // resolved ONCE, so a graph this head cannot read fails at composition rather than per call
        _outputName = head.ResolveOutput(session);
        Capabilities = new ProviderCapabilities
        {
            Accepts = [ProviderKinds.Text],
            Produces = [head.Produces],
            Operations = [ProviderOperation.Complete],
        };
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>What this backend serves, DERIVED from the head it was given. Declaring one kind is how
    /// it tells a router never to send it anything else; every operation it does not implement keeps
    /// <see cref="IModelProvider"/>'s default "I do not serve that" body, so declining costs no code.</summary>
    public ProviderCapabilities Capabilities { get; }

    /// <summary>Always true once constructed: the session is opened EAGERLY, so a missing, truncated or
    /// non-ONNX model has already thrown at composition.</summary>
    public bool IsAvailable => true;

    /// <summary>The sequence length one row of a forward pass takes, including the special tokens; a longer
    /// input is truncated to it, or segmented into windows of it.</summary>
    public int MaxTokens => _windows.MaxTokens;

    /// <summary>Load an ONNX export: a graph plus <c>vocab.txt</c>, with the sequence limit — and, for the
    /// default bi-encoder (pooling) head, pooling mode and normalization — taken from the model's own
    /// <c>config.json</c>, <c>1_Pooling/config.json</c> and <c>modules.json</c>.</summary>
    /// <param name="directory">The model directory.</param>
    /// <param name="options">Knobs; null takes the model's own configuration throughout and embeds.</param>
    /// <exception cref="DirectoryNotFoundException">No such directory.</exception>
    /// <exception cref="FileNotFoundException">No ONNX graph, or no <c>vocab.txt</c> — named individually,
    /// because a partial download is the common case and its unguarded symptom is far away.</exception>
    /// <exception cref="InvalidOperationException">The graph has no output the configured head can
    /// read — for a cross-encoder, one that cannot carry one score per pair.</exception>
    public static OnnxProvider FromDirectory(string directory, OnnxProviderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"No model directory at '{directory}'.");

        options ??= new OnnxProviderOptions();
        var model = OnnxGraph.Resolve(directory, options.ModelFile,
            $"{nameof(OnnxProviderOptions)}.{nameof(OnnxProviderOptions.ModelFile)}");
        var tokenizer = WordPieceTokenizer.FromModelDirectory(directory);
        // the tokenizer answers ids only; which rows continue a word or end a sentence is read off the same file
        var boundaries = TokenBoundaries.FromVocabulary(File.ReadAllLines(Path.Combine(directory, "vocab.txt")));

        // One reader for both heads: a cross-encoder wants only the position limit, but
        // `max_position_embeddings` lives in the same config.json and one reader cannot drift.
        var config = SentenceTransformerConfig.FromDirectory(directory);

        var windows = new WindowedTokenizer(
            tokenizer, boundaries, options.MaxTokens ?? config.MaxTokens, options.Segmentation);
        return new OnnxProvider(new InferenceSession(model), windows, HeadFor(options, config), options.Id);
    }

    /// <summary>Which head serves the declared kind. An unknown one is refused HERE rather than
    /// defaulted: silently embedding for a consumer who asked to rerank is the failure this whole shape
    /// exists to avoid, and a typo in an open vocabulary is the likely way to reach it.</summary>
    private static IOnnxHead HeadFor(OnnxProviderOptions options, SentenceTransformerConfig config)
    {
        if (string.Equals(options.Produces, ProviderKinds.Vector, StringComparison.OrdinalIgnoreCase))
            return new OnnxPoolingHead(options.Pooling ?? config.Pooling, options.Normalize ?? config.Normalize);

        if (string.Equals(options.Produces, ProviderKinds.Score, StringComparison.OrdinalIgnoreCase))
            return new OnnxCrossEncoderHead();

        throw new InvalidOperationException(
            $"{nameof(OnnxProviderOptions)}.{nameof(OnnxProviderOptions.Produces)} is '{options.Produces}', "
            + $"which this backend does not serve. In process it runs a transformer, so it produces "
            + $"{ProviderKinds.Vector} (the model's own pooling) or {ProviderKinds.Score} (a cross-encoder "
            + $"over query/document pairs).");
    }

    /// <inheritdoc />
    /// <remarks>In-process and synchronous: there is no transport to fail, so the only non-Ok outcome a
    /// caller sees from here is a throw the router classifies, or the <c>Unsupported</c> below. The
    /// <see cref="VectorResponse"/> shape is the seam's, not a claim that this backend has verdicts.</remarks>
    public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (_head is not IOnnxVectorHead vectors)
            return Task.FromResult(VectorResponse.Failure(ProviderVerdict.Unsupported, NotServed(ProviderKinds.Vector)));

        return Task.FromResult(request.Texts.Count == 0
            ? new VectorResponse(ProviderVerdict.Ok, [])
            : VectorResponse.Success(vectors.Embed(Run, _outputName, request.Texts)));
    }

    /// <inheritdoc />
    /// <remarks>Same in-process story as the vector call above.</remarks>
    public Task<ScoreResponse> CallAsync(ScoreRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        if (_head is not IOnnxScoreHead scores)
            return Task.FromResult(ScoreResponse.Failure(ProviderVerdict.Unsupported, NotServed(ProviderKinds.Score)));

        return Task.FromResult(request.Documents.Count == 0
            ? new ScoreResponse(ProviderVerdict.Ok, [])
            : ScoreResponse.Success(scores.Score(Run, _outputName, request.Query ?? string.Empty, request.Documents)));
    }

    /// <summary>The engine handed to a head for one call.</summary>
    private OnnxRun Run => new(_session, _windows);

    /// <summary>Why a call was declined. <b>A router should never see this</b> — it selects on
    /// <see cref="Capabilities"/>, which names the one kind this head serves, so reaching here means a
    /// caller went round the router with the wrong call.</summary>
    private string NotServed(string kind) =>
        $"{Id} runs a {_head.Produces} head, not {kind} — one model is one registration, so load "
        + $"the {kind} model under its own AddOnnxProvider call.";

    /// <summary>Releases the native session. Held for the container's life in normal use.</summary>
    public void Dispose() => _session.Dispose();
}
