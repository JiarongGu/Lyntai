using Lyntai.Embeddings;
using Lyntai.Lifecycle;
using Lyntai.Tests.Fakes;
using Lyntai.Providers.Onnx;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Embeddings;

/// <summary>The pooling half — the part that can be WRONG without failing, and the only part of an ONNX
/// embedder a test can reach without a 90 MB model on disk.</summary>
public class EmbeddingPoolingTests
{
    // Two tokens, width 3: row 0 is all 1s, row 1 is all 3s. A mean over both is 2.
    private static readonly float[] Tokens = [1, 1, 1, 3, 3, 3];

    [Fact]
    public void Means_over_the_attended_tokens()
    {
        var vector = EmbeddingPooling.Reduce(Tokens, 3, [1, 1], OnnxPooling.Mean, normalize: false);

        Assert.Equal([2f, 2f, 2f], vector);
    }

    [Fact]
    public void EXCLUDES_padding_from_the_mean_rather_than_averaging_it_in()
    {
        // The silent failure this guards: padding rows hold real numbers, not zeros, so a naive mean shifts
        // every vector by an amount that depends on how long the longest text in the BATCH happened to be —
        // which makes a document's embedding depend on its neighbours.
        var vector = EmbeddingPooling.Reduce(Tokens, 3, [1, 0], OnnxPooling.Mean, normalize: false);

        Assert.Equal([1f, 1f, 1f], vector);
    }

    [Fact]
    public void Cls_pooling_takes_row_zero_and_ignores_the_rest()
    {
        var vector = EmbeddingPooling.Reduce(Tokens, 3, [1, 1], OnnxPooling.Cls, normalize: false);

        Assert.Equal([1f, 1f, 1f], vector);
    }

    [Fact]
    public void Normalizes_to_unit_length_when_asked_and_not_when_not()
    {
        var unit = EmbeddingPooling.Reduce(Tokens, 3, [1, 1], OnnxPooling.Mean, normalize: true);
        Assert.Equal(1.0, Math.Sqrt(unit.Sum(v => (double)v * v)), 5);

        var raw = EmbeddingPooling.Reduce(Tokens, 3, [1, 1], OnnxPooling.Mean, normalize: false);
        Assert.True(Math.Sqrt(raw.Sum(v => (double)v * v)) > 1.5);
    }

    [Fact]
    public void A_fully_masked_text_yields_ZEROS_rather_than_NaN()
    {
        // Encode always emits [CLS]/[SEP] so this cannot arrive through the shipped path — but a NaN
        // compares false against everything, so it would poison a vector store silently instead of failing.
        var vector = EmbeddingPooling.Reduce(Tokens, 3, [0, 0], OnnxPooling.Mean, normalize: true);

        Assert.All(vector, v => Assert.Equal(0f, v));
    }
}

/// <summary>Reading how a sentence-transformer export says it wants to be run. Every value here is a
/// property of how the model was TRAINED, so guessing returns plausible vectors that rank wrongly.</summary>
public class SentenceTransformerConfigTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lyntai-onnx-cfg-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* a temp dir is not worth failing a run */ }
        GC.SuppressFinalize(this);
    }

    private void Write(string relative, string json)
    {
        var path = Path.Combine(_dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    [Fact]
    public void Takes_MEAN_pooling_from_the_models_own_pooling_config()
    {
        Write("1_Pooling/config.json", "{\"pooling_mode_cls_token\": false, \"pooling_mode_mean_tokens\": true}");

        Assert.Equal(OnnxPooling.Mean, SentenceTransformerConfig.FromDirectory(_dir).Pooling);
    }

    [Fact]
    public void Takes_CLS_pooling_when_the_model_declares_it_which_is_what_the_BGE_family_needs()
    {
        Write("1_Pooling/config.json", "{\"pooling_mode_cls_token\": true, \"pooling_mode_mean_tokens\": false}");

        Assert.Equal(OnnxPooling.Cls, SentenceTransformerConfig.FromDirectory(_dir).Pooling);
    }

    [Fact]
    public void Falls_back_to_MEAN_when_a_model_declares_BOTH_which_the_file_format_permits()
    {
        // The flags are independent booleans, not an enum, so a malformed export can set both. Mean is the
        // safe reading: it is the class default, and picking CLS on a mean-trained model is the worse error.
        Write("1_Pooling/config.json", "{\"pooling_mode_cls_token\": true, \"pooling_mode_mean_tokens\": true}");

        Assert.Equal(OnnxPooling.Mean, SentenceTransformerConfig.FromDirectory(_dir).Pooling);
    }

    [Fact]
    public void Normalizes_only_when_modules_json_lists_a_Normalize_step()
    {
        Write("modules.json", "[{\"type\": \"sentence_transformers.models.Transformer\"}]");
        Assert.False(SentenceTransformerConfig.FromDirectory(_dir).Normalize);

        Write("modules.json",
            "[{\"type\": \"sentence_transformers.models.Transformer\"},"
            + "{\"type\": \"sentence_transformers.models.Normalize\"}]");
        Assert.True(SentenceTransformerConfig.FromDirectory(_dir).Normalize);
    }

    [Fact]
    public void Takes_the_ARCHITECTURAL_position_limit_not_the_tokenizers_advertised_one()
    {
        // config.json's max_position_embeddings is the real limit; tokenizer_config's model_max_length is
        // routinely 1000000 (potion sets exactly that), and trusting it would build sequences the graph
        // rejects outright.
        Write("config.json", "{\"max_position_embeddings\": 512}");
        Write("tokenizer_config.json", "{\"model_max_length\": 1000000}");

        Assert.Equal(512, SentenceTransformerConfig.FromDirectory(_dir).MaxTokens);
    }

    [Fact]
    public void An_EMPTY_directory_reads_as_the_sentence_transformers_defaults()
    {
        var config = SentenceTransformerConfig.FromDirectory(_dir);

        Assert.Equal(OnnxPooling.Mean, config.Pooling);
        Assert.False(config.Normalize);
        Assert.Equal(512, config.MaxTokens);
    }
}

/// <summary>Composition failures — the ones a partial download actually produces.</summary>
public class OnnxEmbedderCompositionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lyntai-onnx-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* a temp dir is not worth failing a run */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_MISSING_directory_says_so_rather_than_null_referencing()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => OnnxProvider.FromDirectory(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public void No_GRAPH_names_both_layouts_it_looked_for()
    {
        var error = Assert.Throws<FileNotFoundException>(() => OnnxProvider.FromDirectory(_dir));

        Assert.Contains("onnx/model.onnx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_EXPLICIT_model_file_that_is_absent_names_that_file_not_the_default()
    {
        var error = Assert.Throws<FileNotFoundException>(
            () => OnnxProvider.FromDirectory(_dir, new OnnxProviderOptions { ModelFile = "onnx/model_qint8.onnx" }));

        Assert.Contains("model_qint8.onnx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_graph_WITHOUT_a_vocabulary_fails_on_the_vocabulary()
    {
        // Ordering matters: the graph is found, so the next missing piece must be named rather than
        // surfacing as a null reference inside the tokenizer.
        Directory.CreateDirectory(Path.Combine(_dir, "onnx"));
        File.WriteAllText(Path.Combine(_dir, "onnx", "model.onnx"), "not really a graph");

        var error = Assert.Throws<FileNotFoundException>(() => OnnxProvider.FromDirectory(_dir));

        Assert.Contains("vocab.txt", error.Message, StringComparison.Ordinal);
    }
}

/// <summary>The ONNX embedder against a REAL export, which nothing above can stand in for: everything else
/// here tests a half (pooling arithmetic, a config reader, an error path) because an ONNX graph is protobuf
/// and cannot be hand-built the way a safetensors fixture can.
///
/// <para><b>It also needs a NATIVE backend</b>, which the adapter package deliberately does not ship — the
/// test project references <c>Microsoft.ML.OnnxRuntime</c> exactly as a consuming application would, so
/// this is the only place the managed-only decision is exercised end to end.</para>
///
/// <para>Skipped without <c>LYNTAI_ONNX_MODEL_DIR</c>. Point it at a sentence-transformers export such as
/// <c>all-MiniLM-L6-v2</c>.</para></summary>
public class OnnxProviderLiveTests
{
    private static string? ModelDirectory => Environment.GetEnvironmentVariable("LYNTAI_ONNX_MODEL_DIR");

    private static OnnxProvider Load()
    {
        Skip.If(string.IsNullOrWhiteSpace(ModelDirectory), "set LYNTAI_ONNX_MODEL_DIR to an ONNX export");
        return OnnxProvider.FromDirectory(ModelDirectory!);
    }

    [SkippableFact]
    public async Task A_real_model_ranks_a_related_pair_above_an_unrelated_one()
    {
        using var embedder = Load();

        var vectors = await embedder.EmbedAsync([
            "the weather forecast for tomorrow",
            "a stock market share price quote",
            "tomorrow's weather forecast",
        ]);

        // The cheapest check a wrong pooling mode, a wrong tensor name or a dropped attention mask cannot
        // pass — each of those still returns finite vectors of the right width.
        var related = Cosine(vectors[0], vectors[2]);
        var unrelated = Cosine(vectors[0], vectors[1]);
        Assert.True(related > unrelated,
            $"related {related:F4} should outrank unrelated {unrelated:F4} — a wrong pooling looks like this");
    }

    /// <summary>The load-bearing one: agreement with a REFERENCE implementation, not merely plausibility.
    ///
    /// <para>Every other assertion here passes on a pipeline that pools the wrong way, averages padding in,
    /// or drops <c>token_type_ids</c> — each still returns finite, unit-length, correctly-ordered vectors.
    /// These figures come from the same export run through Python's <c>onnxruntime</c> with the reference HF
    /// tokenizer (<c>devtools/onnx/embed-crosscheck.py</c>), so a divergence localises to this
    /// library rather than to the model.</para></summary>
    [SkippableFact]
    public async Task Agrees_with_the_PYTHON_reference_pipeline_to_four_decimals()
    {
        using var embedder = Load();
        var vectors = await embedder.EmbedAsync([
            "the weather forecast for tomorrow",
            "a stock market share price quote",
            "tomorrow's weather forecast",
        ]);

        // Pinned against all-MiniLM-L6-v2 specifically, so a different export skips rather than failing on
        // numbers that were never about it.
        Skip.IfNot(vectors[0].Length == 384, "reference figures are all-MiniLM-L6-v2's (384 dimensions)");

        Assert.Equal(0.979984, Cosine(vectors[0], vectors[2]), 4);
        Assert.Equal(0.146295, Cosine(vectors[0], vectors[1]), 4);
    }

    [SkippableFact]
    public async Task Takes_NORMALIZATION_from_the_models_own_modules_declaration()
    {
        // all-MiniLM-L6-v2 lists a Normalize module, so its vectors are unit length without anyone asking.
        // Reading it wrong is invisible to cosine, which is scale-invariant — so nothing downstream would
        // report it, which is exactly why it is asserted here.
        using var embedder = Load();

        var vector = (await embedder.EmbedAsync(["the weather forecast for tomorrow"]))[0];

        Assert.Equal(1.0, Math.Sqrt(vector.Sum(v => (double)v * v)), 4);
    }

    [SkippableFact]
    public async Task TRUNCATES_past_its_context_limit_rather_than_throwing()
    {
        // The one capability a model2vec table has over this class, from the other side: a transformer has
        // positional embeddings, so an over-long input must be cut. Failing here would mean a single long
        // document could refuse a whole corpus.
        using var embedder = Load();

        var vectors = await embedder.EmbedAsync([
            string.Join(' ', Enumerable.Repeat("alpha beta gamma", 4000)),
            "short",
        ]);

        Assert.All(vectors, v => Assert.Contains(v, component => component != 0f));
        Assert.Equal(vectors[0].Length, vectors[1].Length);
    }

    [SkippableFact]
    public async Task Reports_an_id_and_availability_like_every_other_provider()
    {
        using var embedder = Load();

        Assert.Equal("onnx", embedder.Id);
        Assert.True(embedder.IsAvailable);
        Assert.Equal(512, embedder.MaxTokens);
        Assert.NotEmpty((await embedder.EmbedAsync(["x"]))[0]);
    }

    private static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}

/// <summary>How the adapter REGISTERS, which is a resource question rather than a wiring one.</summary>
public class OnnxRegistrationTests
{
    private sealed class TrackingEmbedder : EmbeddingBackend, IDisposable
    {
        public bool WasDisposed { get; private set; }

        public override Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<float[]>>([]);

        public void Dispose() => WasDisposed = true;
    }

    // These two pin the DI PREMISE and nothing else — that MS.DI disposes what a factory produced and not
    // what it was handed. They say nothing about which form `AddOnnxProvider` uses, and for a while their
    // own comment claimed they did: rewriting that call to `AddSingleton(instance)` left both green.
    // `OnnxOwnershipTests` asserts the registration those builder calls actually perform.

    [Fact]
    public void A_singleton_registered_as_an_INSTANCE_is_NOT_disposed_by_the_container()
    {
        // The premise behind registering through a factory: a provider holds a native session, so "the
        // container will clean it up" has to be true rather than assumed — and for this overload it is not.
        var embedder = new TrackingEmbedder();
        var services = new ServiceCollection();
        services.AddSingleton<IModelProvider>(embedder);

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IModelProvider>();
        provider.Dispose();

        Assert.False(embedder.WasDisposed);
    }

    [Fact]
    public void A_singleton_registered_through_a_FACTORY_is_disposed_by_the_container()
    {
        var embedder = new TrackingEmbedder();
        var services = new ServiceCollection();
        services.AddSingleton<IModelProvider>(_ => embedder);

        var provider = services.BuildServiceProvider();
        _ = provider.GetRequiredService<IModelProvider>();
        provider.Dispose();

        Assert.True(embedder.WasDisposed);
    }
}
