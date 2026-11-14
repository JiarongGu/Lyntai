using Lyntai.Inference;
using Lyntai.Memory;
using System.Text.Json;
using Lyntai.Text;

namespace Lyntai.Providers.Model2Vec;

/// <summary>Knobs for <see cref="Model2VecProvider"/>.</summary>
public sealed class Model2VecProviderOptions
{
    /// <summary>Whether to L2-normalize each vector. Null reads the model's own <c>config.json</c>, which is
    /// what a <c>model2vec</c> export states and what its reference implementation honours — set this only
    /// to override a model that declares the wrong thing.</summary>
    public bool? Normalize { get; set; }

    /// <summary>The provider id this backend reports as <see cref="IModelProvider.Id"/> — the backend's name,
    /// <c>model2vec</c>, by default. Give each registration a distinct one when a deployment registers more
    /// than one: the first-wins router never reaches a second backend under the same id.</summary>
    public string Id { get; set; } = "model2vec";

    /// <summary>Whether the unknown token's row counts in the mean. False by default, which is what the reference
    /// <c>model2vec</c> does: it drops the unknown id before pooling, so a word the vocabulary cannot piece moves no
    /// vector and a text of only such words is the zero vector. True reproduces the vectors earlier releases of this
    /// provider stored, which averaged that row in. A text with an unknown word embeds differently under the two
    /// settings, so a stored collection is comparable only with the setting that wrote it — change it together with
    /// the deployment's declared vector space (<c>UseVectorSpace</c>), or re-embed.</summary>
    public bool IncludeUnknownToken { get; set; }

    /// <summary>Whether to pool every token of a text rather than cut it as the reference <c>model2vec</c> does. False by default:
    /// a text is cut to its first <c>max_length</c> × median-token-length characters, then to its first <c>max_length</c> tokens,
    /// and only then is the unknown token dropped, where <c>max_length</c> is the model's <c>config.json</c> value (512 when it
    /// names none, no cut when it names null). True reproduces the vectors earlier releases of this provider stored. A text past
    /// the cut embeds differently under the two settings, so a stored collection is comparable only with the setting that wrote
    /// it — change it together with the deployment's declared vector space (<c>UseVectorSpace</c>), or re-embed.</summary>
    public bool EmbedWholeText { get; set; }
}

/// <summary>An embedding backend that runs IN PROCESS with no server, no GPU and no port: a
/// <c>model2vec</c> static lookup table, mean-pooled.
///
/// <para><b>The case for it is OPERATIONAL, not quality or speed.</b> Encode-only vectors are
/// byte-identical across devices, so moving a model in-process cannot change a retrieval score, and a local
/// HTTP call is not where the latency is. What it buys is no second process to ship and supervise, no port to
/// conflict, and a lifetime tied to the application's (<c>docs/deployment-shapes.md</c>).</para>
///
/// <para><b>What it costs is a property of the WORKLOAD</b> — small on the memory default, larger on a
/// purely embedding-bound selective task. Read the arm that matches yours, not a headline
/// (<c>docs/memory-measurements.md</c> §5).</para>
///
/// <para><b>It CUTS a long text as the reference does, rather than refusing it</b>: a lookup table has no positional
/// embeddings, so nothing forces a limit, but the reference <c>model2vec</c> pools only a text's first <c>max_length</c>
/// tokens (512 unless the model's <c>config.json</c> says otherwise), and a query embedded here must match a corpus embedded
/// there. <see cref="Model2VecProviderOptions.EmbedWholeText"/> pools every token instead.</para>
///
/// <para><b>No PCA or Zipf weighting is applied at inference.</b> A <c>model2vec</c> export bakes both into
/// the table when it is built, so the runtime is a lookup and a mean. This reads <c>config.json</c> only for
/// <c>normalize</c> and <c>max_length</c>.</para></summary>
public sealed class Model2VecProvider : IVectorProvider
{
    /// <summary>The reference's <c>DEFAULT_MAX_LENGTH</c>, taken when <c>config.json</c> names no <c>max_length</c>.</summary>
    private const int DefaultMaxLength = 512;

    private readonly WordPieceTokenizer _tokenizer;
    private readonly SafetensorsTable _table;
    private readonly bool _normalize;
    private readonly int? _skippedId;
    private readonly (int Tokens, int CodePoints)? _cut;

    private Model2VecProvider(WordPieceTokenizer tokenizer, SafetensorsTable table, bool normalize, string id,
        bool includeUnknownToken, (int Tokens, int CodePoints)? cut)
    {
        _tokenizer = tokenizer;
        _table = table;
        _normalize = normalize;
        _skippedId = includeUnknownToken ? null : tokenizer.UnknownId;
        _cut = cut;
        Id = id;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>Text in, VECTORS out — which is the whole of what makes this a vector backend. Producing only
    /// <see cref="ProviderKinds.Vector"/> is how it tells a router never to send it a chat or a render;
    /// every method it does not implement keeps <see cref="IModelProvider"/>'s default "I do not serve
    /// that" body, so declining costs no code.</summary>
    public ProviderCapabilities Capabilities { get; } = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Vector],
        Operations = [ProviderOperation.Complete],
    };

    /// <summary>Always true once constructed. The table is loaded EAGERLY, so a model that is missing or
    /// truncated has already thrown at composition — there is no later state in which this becomes
    /// false.</summary>
    public bool IsAvailable => true;

    /// <summary>The vector width this model produces.</summary>
    public int Dimensions => _table.Dimensions;

    /// <summary>Load a <c>model2vec</c> model from a directory holding <c>model.safetensors</c>,
    /// <c>vocab.txt</c> and (optionally) <c>config.json</c> — the layout of a downloaded
    /// <c>minishlab/potion-*</c> or <c>static-retrieval-*</c> model.</summary>
    /// <param name="directory">The model directory.</param>
    /// <param name="options">Knobs; null takes the model's own configuration.</param>
    /// <exception cref="DirectoryNotFoundException">No such directory.</exception>
    /// <exception cref="FileNotFoundException">A required file is missing, named individually so the fix is
    /// obvious — a partial download is the common case and its symptom is otherwise a null reference.</exception>
    public static Model2VecProvider FromDirectory(string directory, Model2VecProviderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"No model directory at '{directory}'.");
        options ??= new Model2VecProviderOptions();

        var weights = Path.Combine(directory, "model.safetensors");
        var vocabulary = Path.Combine(directory, "vocab.txt");
        foreach (var required in new[] { weights, vocabulary })
            if (!File.Exists(required))
                throw new FileNotFoundException(
                    $"'{Path.GetFileName(required)}' is missing from '{directory}'. A model2vec model needs "
                    + "model.safetensors and vocab.txt; check the download completed.", required);

        var table = SafetensorsTable.Load(weights);

        // WordPiece, because the shipped static models tokenize with a BERT vocabulary — potion-* declares
        // `baai/bge-base-en-v1.5` as its tokenizer_name. The table was BUILT against these ids, so the
        // tokenizer is part of the model rather than a choice, and `FromModelDirectory` takes its rules
        // from the model's own tokenizer_config.json for the same reason.
        var tokenizer = WordPieceTokenizer.FromModelDirectory(directory);

        var (normalize, maxLength) = ReadConfig(directory);
        (int, int)? cut = options.EmbedWholeText || maxLength is not { } tokens
            ? null
            : (tokens, (int)Math.Min(int.MaxValue, (long)tokens * MedianTokenLength(vocabulary)));
        return new Model2VecProvider(
            tokenizer, table, options.Normalize ?? normalize, options.Id, options.IncludeUnknownToken, cut);
    }

    /// <inheritdoc />
    /// <remarks>A lookup and a mean, in process: there is no transport to fail, so the only non-Ok outcome
    /// a caller sees from here is a throw the router classifies.</remarks>
    public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(request.Texts.Count == 0
            ? new VectorResponse(ProviderVerdict.Ok, [])
            : VectorResponse.Success([.. request.Texts.Select(Embed)]));
    }

    /// <summary>Mean of the rows the text's tokens select — those inside the cut, unless
    /// <see cref="Model2VecProviderOptions.EmbedWholeText"/> — the unknown token's left out unless
    /// <see cref="Model2VecProviderOptions.IncludeUnknownToken"/> says otherwise. <b>A text with no usable token
    /// — empty, or every word unknown — yields a ZERO vector rather than throwing</b>: it compares as maximally
    /// dissimilar to everything, and a store full of documents must not be refused over one of them.</summary>
    private float[] Embed(string text)
    {
        var vector = new float[_table.Dimensions];
        if (string.IsNullOrWhiteSpace(text)) return vector;

        // the reference's order: the characters, then the tokens, unknown ones counted; the unknown id is dropped after
        var ids = _tokenizer.EncodeToIds(_cut is { } cut ? FirstCodePoints(text, cut.CodePoints) : text);
        var kept = _cut is { } limit ? Math.Min(limit.Tokens, ids.Count) : ids.Count;

        var span = vector.AsSpan();
        var counted = 0;
        for (var i = 0; i < kept; i++)
        {
            var id = ids[i];
            if (id == _skippedId) continue;
            if (_table.AccumulateInto(id, span)) counted++;   // an id past the table adds no row, so it is no divisor
        }

        if (counted == 0) return vector;
        for (var i = 0; i < vector.Length; i++) vector[i] /= counted;

        if (_normalize) VectorMath.NormalizeInPlace(vector);
        return vector;
    }

    /// <summary>The model's own <c>normalize</c> flag and <c>max_length</c>. Normalize defaults to TRUE when there is no
    /// config to read — every shipped `model2vec` export sets it, and an un-normalized vector silently changes what cosine
    /// means against a corpus embedded by the reference implementation. <c>max_length</c> is the reference's: absent is
    /// <see cref="DefaultMaxLength"/>, JSON null is no cut, and a value that is not a positive whole number reads as
    /// absent.</summary>
    private static (bool Normalize, int? MaxLength) ReadConfig(string directory)
    {
        var path = Path.Combine(directory, "config.json");
        if (!File.Exists(path)) return (true, DefaultMaxLength);

        try
        {
            using var config = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = config.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return (true, DefaultMaxLength);
            var normalize = !root.TryGetProperty("normalize", out var flag) || flag.ValueKind != JsonValueKind.False;
            int? maxLength = !root.TryGetProperty("max_length", out var length) ? DefaultMaxLength
                : length.ValueKind == JsonValueKind.Null ? null
                : length.ValueKind == JsonValueKind.Number && length.TryGetInt32(out var tokens) && tokens > 0 ? tokens
                : DefaultMaxLength;
            return (normalize, maxLength);
        }
        catch (JsonException)
        {
            return (true, DefaultMaxLength);
        }
    }

    /// <summary>The reference's <c>median_token_length</c>: the median length, in code points, of the vocabulary's distinct
    /// tokens, the two middle ones averaged and truncated when there is an even number.</summary>
    private static int MedianTokenLength(string vocabulary)
    {
        var lengths = File.ReadLines(vocabulary).Distinct(StringComparer.Ordinal)
            .Select(token => token.EnumerateRunes().Count()).Order().ToArray();
        if (lengths.Length == 0) return 0;
        var middle = lengths.Length / 2;
        return lengths.Length % 2 == 1 ? lengths[middle] : (lengths[middle - 1] + lengths[middle]) / 2;
    }

    /// <summary>The text's first <paramref name="count"/> code points, as the reference's string slice takes them, so a
    /// surrogate pair is never cut in half.</summary>
    private static string FirstCodePoints(string text, int count)
    {
        var end = 0;
        for (var taken = 0; end < text.Length && taken < count; taken++)
            end += char.IsSurrogatePair(text, end) ? 2 : 1;
        return end == text.Length ? text : text[..end];
    }
}
