using Lyntai.Lifecycle;
using Lyntai.Text;
using Microsoft.ML.OnnxRuntime;

namespace Lyntai.Providers.Onnx;

/// <summary>An <see cref="IModelProvider"/> running a CROSS-ENCODER in process through ONNX Runtime — a
/// query and a document through one transformer, one relevance logit out. No HTTP endpoint, no server, no
/// port.
///
/// <para><b>It is a different backend from <see cref="OnnxProvider"/>, not a mode of it.</b> An embedder
/// answers "what does this text mean" once per text and a cross-encoder answers "does this document answer
/// this query" once per PAIR, so the two produce different <see cref="ProviderKinds"/> and cost differently:
/// n documents are n forward passes here, against one each.</para>
///
/// <para><b>What reaching it takes is a registration, not a seam</b> —
/// <c>AddMemoryScoringVerification</c> selects any backend declaring <see cref="ProviderKinds.Score"/>
/// (<c>docs/DECISIONS.md</c> D139), so this serves the memory verification seam without speaking HTTP.</para>
///
/// <para><b>Inference runs on the calling thread</b>, as with the embedder: the async signature is the
/// seam's, not a promise to yield.</para></summary>
public sealed class OnnxCrossEncoder : IModelProvider, IDisposable
{
    private readonly InferenceSession _session;
    private readonly WordPieceTokenizer _tokenizer;
    private readonly int _maxTokens;
    private readonly string _outputName;

    private OnnxCrossEncoder(
        InferenceSession session, WordPieceTokenizer tokenizer, int maxTokens, OnnxCrossEncoderOptions options)
    {
        _session = session;
        _tokenizer = tokenizer;
        _maxTokens = options.MaxTokens ?? maxTokens;
        Id = options.Id;
        _outputName = ScoreOutputName(session);
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>The declaration, held apart from the instance so a test can assert that the memory scoring
    /// seam selects it without a model on disk — it is the whole of what makes this backend reachable.</summary>
    internal static readonly ProviderCapabilities Declared = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Score],
        Operations = [ProviderOperation.Complete],
    };

    /// <summary>Text in, SCORES out. Producing only <see cref="ProviderKinds.Score"/> is how it tells a
    /// router never to send it a chat, an embedding or a render.</summary>
    public ProviderCapabilities Capabilities => Declared;

    /// <summary>Always true once constructed: the session is opened EAGERLY, so a missing, truncated or
    /// non-ONNX model has already thrown at composition.</summary>
    public bool IsAvailable => true;

    /// <summary>The sequence length a PAIR truncates at, including all three special tokens.</summary>
    public int MaxTokens => _maxTokens;

    /// <summary>Load a cross-encoder export: an ONNX graph plus <c>vocab.txt</c>, with the position limit
    /// taken from the model's own <c>config.json</c>.</summary>
    /// <param name="directory">The model directory.</param>
    /// <param name="options">Knobs; null takes the model's own configuration.</param>
    /// <exception cref="DirectoryNotFoundException">No such directory.</exception>
    /// <exception cref="FileNotFoundException">No ONNX graph, or no <c>vocab.txt</c> — named individually,
    /// because a partial download is the common case and its unguarded symptom is far away.</exception>
    /// <exception cref="InvalidOperationException">The graph has no output this can read a score from.</exception>
    public static OnnxCrossEncoder FromDirectory(string directory, OnnxCrossEncoderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"No model directory at '{directory}'.");

        options ??= new OnnxCrossEncoderOptions();
        var model = OnnxGraph.Resolve(directory, options.ModelFile,
            $"{nameof(OnnxCrossEncoderOptions)}.{nameof(OnnxCrossEncoderOptions.ModelFile)}");
        var tokenizer = WordPieceTokenizer.FromModelDirectory(directory);

        // Only the position limit is wanted here — pooling and normalization are the embedder's half of that
        // reader — but max_position_embeddings lives in the same config.json and one reader cannot drift.
        var maxTokens = SentenceTransformerConfig.FromDirectory(directory).MaxTokens;

        return new OnnxCrossEncoder(new InferenceSession(model), tokenizer, maxTokens, options);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<double>> ScoreAsync(
        string query, IReadOnlyList<string> documents, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<double>>(
            documents.Count == 0 ? [] : Score(query ?? string.Empty, documents));
    }

    /// <summary>One batched forward pass over <c>[CLS] query [SEP] document [SEP]</c>: encode each pair, pad
    /// to the longest, run, read one logit per row.
    ///
    /// <para><b>The segment ids are the point.</b> They are what tells the model which half is the question,
    /// and a runtime that zeroes them scores the pair as one undifferentiated string — which is how the same
    /// weights rank a published reference pair BACKWARDS through llama.cpp
    /// (<c>docs/memory-measurements.md</c> §5).</para></summary>
    private double[] Score(string query, IReadOnlyList<string> documents)
    {
        var encodings = new WordPieceEncoding[documents.Count];
        for (var i = 0; i < documents.Count; i++)
            encodings[i] = _tokenizer.Encode(query, documents[i] ?? string.Empty, _maxTokens);

        var width = encodings.Max(e => e.Ids.Length);
        using var results = _session.Run(OnnxGraph.Feed(_session, encodings, width), [_outputName]);
        var head = results[0].AsTensor<float>();
        return CrossEncoderLogits.Read(head.ToArray(), head.Dimensions, documents.Count);
    }

    /// <summary>The classification head's output — <c>logits</c> by name where the export gives one, and
    /// otherwise the single output it has.</summary>
    private static string ScoreOutputName(InferenceSession session)
    {
        if (session.OutputMetadata.ContainsKey("logits")) return "logits";
        if (session.OutputMetadata.Count == 1) return session.OutputMetadata.Keys.First();

        throw new InvalidOperationException(
            $"This graph has no 'logits' output and {session.OutputMetadata.Count} others to choose from "
            + $"({string.Join(", ", session.OutputMetadata.Keys)}). A bi-encoder is the likeliest cause — it "
            + $"embeds rather than scores, so load it with {nameof(OnnxProvider)} instead.");
    }

    /// <summary>Releases the native session. Held for the container's life in normal use.</summary>
    public void Dispose() => _session.Dispose();
}
