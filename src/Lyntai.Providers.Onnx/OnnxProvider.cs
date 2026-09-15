using Lyntai.Embeddings;
using Lyntai.Lifecycle;
using Lyntai.Text;
using Microsoft.ML.OnnxRuntime;

namespace Lyntai.Providers.Onnx;

/// <summary>An <see cref="IModelProvider"/> running a TRANSFORMER in process through ONNX Runtime — no
/// HTTP endpoint, no server, no port.
///
/// <para><b>What it buys over the static class, measured.</b> On tool routing a 25,008,064 B transformer
/// reads 78.6% at three options against a 30,236,760 B <c>model2vec</c> table's 69.0%
/// (<c>docs/memory-measurements.md</c> §5). What it costs is ~16 MB of native runtime and this package's
/// trim/AOT claim.</para>
///
/// <para><b>It HAS a context limit</b>, which the static class does not: a BERT encoder has positional
/// embeddings, so input past <see cref="OnnxProviderOptions.MaxTokens"/> is truncated.</para>
///
/// <para><b>Inference runs on the calling thread.</b> The async signature is the seam's, not a promise to
/// yield — a batch of long documents is CPU-bound for tens of milliseconds. Wrap the call if that matters
/// to your scheduler.</para></summary>
public sealed class OnnxProvider : IModelProvider, IDisposable
{
    private readonly InferenceSession _session;
    private readonly WordPieceTokenizer _tokenizer;
    private readonly SentenceTransformerConfig _config;
    private readonly OnnxPooling _pooling;
    private readonly bool _normalize;
    private readonly int _maxTokens;
    private readonly string _outputName;

    private OnnxProvider(InferenceSession session, WordPieceTokenizer tokenizer,
        SentenceTransformerConfig config, OnnxProviderOptions options)
    {
        _session = session;
        _tokenizer = tokenizer;
        _config = config;
        _pooling = options.Pooling ?? config.Pooling;
        _normalize = options.Normalize ?? config.Normalize;
        _maxTokens = options.MaxTokens ?? config.MaxTokens;
        Id = options.Id;
        _outputName = TokenOutputName(session);
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>Text in, VECTORS out — which is the whole of what makes this an embedder. Producing only
    /// <see cref="ProviderKinds.Vector"/> is how it tells a router never to send it a chat or a render;
    /// every method it does not implement keeps <see cref="IModelProvider"/>'s default "I do not serve
    /// that" body, so declining costs no code.</summary>
    public ProviderCapabilities Capabilities { get; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Vector],
        Operations = [ProviderOperation.Complete],
    };

    /// <summary>Always true once constructed: the session is opened EAGERLY, so a missing, truncated or
    /// non-ONNX model has already thrown at composition.</summary>
    public bool IsAvailable => true;

    /// <summary>The sequence length this embedder truncates at, including both special tokens.</summary>
    public int MaxTokens => _maxTokens;

    /// <summary>Load a sentence-transformer export: an ONNX graph plus <c>vocab.txt</c>, with pooling and
    /// normalization taken from the model's own <c>1_Pooling/config.json</c> and <c>modules.json</c>.</summary>
    /// <param name="directory">The model directory.</param>
    /// <param name="options">Knobs; null takes the model's own configuration throughout.</param>
    /// <exception cref="DirectoryNotFoundException">No such directory.</exception>
    /// <exception cref="FileNotFoundException">No ONNX graph, or no <c>vocab.txt</c> — named individually,
    /// because a partial download is the common case and its unguarded symptom is far away.</exception>
    public static OnnxProvider FromDirectory(string directory, OnnxProviderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"No model directory at '{directory}'.");

        options ??= new OnnxProviderOptions();
        var model = OnnxGraph.Resolve(directory, options.ModelFile,
            $"{nameof(OnnxProviderOptions)}.{nameof(OnnxProviderOptions.ModelFile)}");
        var tokenizer = WordPieceTokenizer.FromModelDirectory(directory);
        var config = SentenceTransformerConfig.FromDirectory(directory);

        return new OnnxProvider(new InferenceSession(model), tokenizer, config, options);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<float[]>>(texts.Count == 0 ? [] : Embed(texts));
    }

    /// <summary>One batched forward pass: encode, pad to the longest, run, pool each row.</summary>
    private float[][] Embed(IReadOnlyList<string> texts)
    {
        var encodings = new WordPieceEncoding[texts.Count];
        for (var i = 0; i < texts.Count; i++) encodings[i] = _tokenizer.Encode(texts[i] ?? string.Empty, _maxTokens);

        var width = encodings.Max(e => e.Ids.Length);
        using var results = _session.Run(OnnxGraph.Feed(_session, encodings, width), [_outputName]);
        var hidden = results[0].AsTensor<float>();
        var hiddenSize = hidden.Dimensions[2];
        var flat = hidden.ToArray();

        var vectors = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
        {
            var block = flat.AsSpan(i * width * hiddenSize, width * hiddenSize);
            vectors[i] = EmbeddingPooling.Reduce(
                block, hiddenSize, PaddedMask(encodings[i], width), _pooling, _normalize);
        }

        return vectors;
    }

    /// <summary>The attention mask widened to the batch, zero over the padding — which is what tells
    /// <see cref="EmbeddingPooling"/> not to average the padded rows in.</summary>
    private static int[] PaddedMask(WordPieceEncoding encoding, int width)
    {
        var mask = new int[width];
        encoding.AttentionMask.CopyTo(mask, 0);
        return mask;
    }

    /// <summary>The per-token output, by name where the export gives one and by position otherwise.
    /// <b>Never the pooled output</b>: some exports add a <c>sentence_embedding</c>, but taking it would
    /// silently ignore the configured pooling and return whatever the exporter chose.</summary>
    private static string TokenOutputName(InferenceSession session)
    {
        foreach (var candidate in new[] { "last_hidden_state", "token_embeddings" })
            if (session.OutputMetadata.ContainsKey(candidate)) return candidate;
        return session.OutputMetadata.Keys.First();
    }

    /// <summary>Releases the native session. Held for the container's life in normal use.</summary>
    public void Dispose() => _session.Dispose();
}
