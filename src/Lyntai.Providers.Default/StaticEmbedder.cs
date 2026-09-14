using Lyntai.Lifecycle;
using System.Text.Json;
using Lyntai.Text;

namespace Lyntai.Embeddings.Static;

/// <summary>Knobs for <see cref="StaticEmbedder"/>.</summary>
public sealed class StaticEmbedderOptions
{
    /// <summary>Whether to L2-normalize each vector. Null reads the model's own <c>config.json</c>, which is
    /// what a <c>model2vec</c> export states and what its reference implementation honours — set this only
    /// to override a model that declares the wrong thing.</summary>
    public bool? Normalize { get; set; }

    /// <summary>The provider id this backend reports as <see cref="IModelProvider.Id"/>. Give it a
    /// distinct value when a deployment registers more than one embedder, so a diagnostic can say which
    /// one produced a vector.</summary>
    public string Id { get; set; } = "static";
}

/// <summary>An <see cref="IEmbedder"/> that runs IN PROCESS with no server, no GPU and no port: a
/// <c>model2vec</c> static lookup table, mean-pooled.
///
/// <para><b>The case for it is OPERATIONAL, not quality or speed.</b> Encode-only vectors are
/// byte-identical across devices, so moving a model in-process cannot change a retrieval score; and a local
/// HTTP call with <c>UseProxy = false</c> measures 0.4 ms, so there is no latency to win. What it buys is
/// no second process to ship and supervise, no port to conflict, and a lifetime tied to the application's —
/// decisive for a distributed app and invisible to a benchmark (<c>docs/deployment-shapes.md</c>).</para>
///
/// <para><b>What the class costs, measured.</b> On the memory workload `potion-base-8M` (30,236,760 B) is
/// <b>0.5 points</b> behind a 333,590,944 B server-hosted embedder on the shipped default; on a purely
/// embedding-bound selective task it is about <b>12</b> points behind. <b>How much an embedder is worth is a
/// property of the ARM</b> — read the one that matches your workload, not the headline
/// (<c>docs/memory-measurements.md</c> §5).</para>
///
/// <para><b>It has NO context limit</b>, unlike every sub-100 MB transformer embedder, which reject an input
/// past 512 tokens: a lookup table has no positional embeddings, so a long document is simply more rows to
/// average.</para>
///
/// <para><b>No PCA or Zipf weighting is applied at inference.</b> A <c>model2vec</c> export bakes both into
/// the table when it is built, so the runtime is a lookup and a mean. This reads <c>config.json</c> only for
/// <c>normalize</c>.</para></summary>
public sealed class StaticEmbedder : IModelProvider, IEmbedder
{
    private readonly WordPieceTokenizer _tokenizer;
    private readonly SafetensorsTable _table;
    private readonly bool _normalize;

    private StaticEmbedder(WordPieceTokenizer tokenizer, SafetensorsTable table, bool normalize, string id)
    {
        _tokenizer = tokenizer;
        _table = table;
        _normalize = normalize;
        Id = id;
    }

    /// <inheritdoc />
    public string Id { get; }

    /// <summary>Text in, vectors out — and nothing else. Declaring only <see cref="ProviderOperation.Embed"/>
    /// is how this backend tells a router never to send it a chat or a render; every other operation keeps
    /// <see cref="IModelProvider"/>'s default "I do not serve that" body, so declining costs no code.</summary>
    public ProviderCapabilities Capabilities { get; } = new()
    {
        Kinds = [ProviderKinds.Text],
        Operations = [ProviderOperation.Embed],
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
    public static StaticEmbedder FromDirectory(string directory, StaticEmbedderOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        if (!Directory.Exists(directory)) throw new DirectoryNotFoundException($"No model directory at '{directory}'.");

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

        return new StaticEmbedder(
            tokenizer, table, options?.Normalize ?? NormalizeFromConfig(directory), options?.Id ?? "static");
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(texts);
        ct.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<float[]>>([.. texts.Select(Embed)]);
    }

    /// <summary>Mean of the rows the text's tokens select. <b>A text with no usable token yields a ZERO
    /// vector rather than throwing</b> — it is what an empty string means, it compares as maximally
    /// dissimilar to everything, and a store full of documents must not be refused over one of them.</summary>
    private float[] Embed(string text)
    {
        var vector = new float[_table.Dimensions];
        if (string.IsNullOrWhiteSpace(text)) return vector;

        var span = vector.AsSpan();
        var counted = 0;
        foreach (var id in _tokenizer.EncodeToIds(text))
        {
            _table.AccumulateInto(id, span);
            counted++;
        }

        if (counted == 0) return vector;
        for (var i = 0; i < vector.Length; i++) vector[i] /= counted;

        if (!_normalize) return vector;
        double sum = 0;
        foreach (var v in vector) sum += (double)v * v;
        var length = Math.Sqrt(sum);
        if (length > 0)
            for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / length);
        return vector;
    }

    /// <summary>The model's own <c>normalize</c> flag, defaulting to TRUE when there is no config to read —
    /// every shipped `model2vec` export sets it, and an un-normalized vector silently changes what cosine
    /// means against a corpus embedded by the reference implementation.</summary>
    private static bool NormalizeFromConfig(string directory)
    {
        var path = Path.Combine(directory, "config.json");
        if (!File.Exists(path)) return true;

        try
        {
            using var config = JsonDocument.Parse(File.ReadAllBytes(path));
            return !config.RootElement.TryGetProperty("normalize", out var flag)
                   || flag.ValueKind != JsonValueKind.False;
        }
        catch (JsonException)
        {
            return true;
        }
    }
}
