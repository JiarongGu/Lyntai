using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using Lyntai.Providers.Onnx;
using Microsoft.Extensions.DependencyInjection;
using static Lyntai.Tests.Fakes.VectorMath;

namespace Lyntai.Tests.Embeddings;

/// <summary>The pooling half — the part that can be WRONG without failing, and the only part of an ONNX
/// vector backend a test can reach without a 90 MB model on disk.</summary>
public class VectorPoolingTests
{
    // Two tokens, width 3: row 0 is all 1s, row 1 is all 3s. A mean over both is 2.
    private static readonly float[] Tokens = [1, 1, 1, 3, 3, 3];

    [Fact]
    public void Means_over_the_attended_tokens()
    {
        var vector = VectorPooling.Reduce(Tokens, 3, [1, 1], OnnxPooling.Mean, normalize: false);

        Assert.Equal([2f, 2f, 2f], vector);
    }

    [Fact]
    public void EXCLUDES_padding_from_the_mean_rather_than_averaging_it_in()
    {
        // The silent failure this guards: padding rows hold real numbers, not zeros, so a naive mean shifts
        // every vector by an amount that depends on how long the longest text in the BATCH happened to be —
        // which makes a document's embedding depend on its neighbours.
        var vector = VectorPooling.Reduce(Tokens, 3, [1, 0], OnnxPooling.Mean, normalize: false);

        Assert.Equal([1f, 1f, 1f], vector);
    }

    [Fact]
    public void Cls_pooling_takes_row_zero_and_ignores_the_rest()
    {
        var vector = VectorPooling.Reduce(Tokens, 3, [1, 1], OnnxPooling.Cls, normalize: false);

        Assert.Equal([1f, 1f, 1f], vector);
    }

    [Fact]
    public void Normalizes_to_unit_length_when_asked_and_not_when_not()
    {
        var unit = VectorPooling.Reduce(Tokens, 3, [1, 1], OnnxPooling.Mean, normalize: true);
        Assert.Equal(1.0, Math.Sqrt(unit.Sum(v => (double)v * v)), 5);

        var raw = VectorPooling.Reduce(Tokens, 3, [1, 1], OnnxPooling.Mean, normalize: false);
        Assert.True(Math.Sqrt(raw.Sum(v => (double)v * v)) > 1.5);
    }

    [Fact]
    public void A_fully_masked_text_yields_ZEROS_rather_than_NaN()
    {
        // Encode always emits [CLS]/[SEP] so this cannot arrive through the shipped path — but a NaN
        // compares false against everything, so it would poison a vector store silently instead of failing.
        var vector = VectorPooling.Reduce(Tokens, 3, [0, 0], OnnxPooling.Mean, normalize: true);

        Assert.All(vector, v => Assert.Equal(0f, v));
    }
}

/// <summary>Reading how a sentence-transformer export says it wants to be run. Every value here is a
/// property of how the model was TRAINED, so guessing returns plausible vectors that rank wrongly.</summary>
public class SentenceTransformerConfigTests : IDisposable
{
    private readonly ScratchDir _scratch = new("onnx-cfg");

    private string Dir => _scratch.Path;

    public void Dispose() => _scratch.Dispose();

    private void Write(string relative, string json)
    {
        var path = Path.Combine(Dir, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, json);
    }

    [Fact]
    public void Takes_MEAN_pooling_from_the_models_own_pooling_config()
    {
        Write("1_Pooling/config.json", "{\"pooling_mode_cls_token\": false, \"pooling_mode_mean_tokens\": true}");

        Assert.Equal(OnnxPooling.Mean, SentenceTransformerConfig.FromDirectory(Dir).Pooling);
    }

    [Fact]
    public void Takes_CLS_pooling_when_the_model_declares_it_which_is_what_the_BGE_family_needs()
    {
        Write("1_Pooling/config.json", "{\"pooling_mode_cls_token\": true, \"pooling_mode_mean_tokens\": false}");

        Assert.Equal(OnnxPooling.Cls, SentenceTransformerConfig.FromDirectory(Dir).Pooling);
    }

    [Fact]
    public void Falls_back_to_MEAN_when_a_model_declares_BOTH_which_the_file_format_permits()
    {
        // The flags are independent booleans, not an enum, so a malformed export can set both. Mean is the
        // safe reading: it is the class default, and picking CLS on a mean-trained model is the worse error.
        Write("1_Pooling/config.json", "{\"pooling_mode_cls_token\": true, \"pooling_mode_mean_tokens\": true}");

        Assert.Equal(OnnxPooling.Mean, SentenceTransformerConfig.FromDirectory(Dir).Pooling);
    }

    [Fact]
    public void Normalizes_only_when_modules_json_lists_a_Normalize_step()
    {
        Write("modules.json", "[{\"type\": \"sentence_transformers.models.Transformer\"}]");
        Assert.False(SentenceTransformerConfig.FromDirectory(Dir).Normalize);

        Write("modules.json",
            "[{\"type\": \"sentence_transformers.models.Transformer\"},"
            + "{\"type\": \"sentence_transformers.models.Normalize\"}]");
        Assert.True(SentenceTransformerConfig.FromDirectory(Dir).Normalize);
    }

    [Fact]
    public void Takes_the_ARCHITECTURAL_position_limit_not_the_tokenizers_advertised_one()
    {
        // config.json's max_position_embeddings is the real limit; tokenizer_config's model_max_length is
        // routinely 1000000 (potion sets exactly that), and trusting it would build sequences the graph
        // rejects outright.
        Write("config.json", "{\"max_position_embeddings\": 512}");
        Write("tokenizer_config.json", "{\"model_max_length\": 1000000}");

        Assert.Equal(512, SentenceTransformerConfig.FromDirectory(Dir).MaxTokens);
    }

    [Fact]
    public void An_EMPTY_directory_reads_as_the_sentence_transformers_defaults()
    {
        var config = SentenceTransformerConfig.FromDirectory(Dir);

        Assert.Equal(OnnxPooling.Mean, config.Pooling);
        Assert.False(config.Normalize);
        Assert.Equal(512, config.MaxTokens);
    }
}

/// <summary>Composition failures — the ones a partial download actually produces.</summary>
public class OnnxProviderCompositionTests : IDisposable
{
    private readonly ScratchDir _scratch = new("onnx");

    private string Dir => _scratch.Path;

    public void Dispose() => _scratch.Dispose();

    [Fact]
    public void A_MISSING_directory_says_so_rather_than_null_referencing()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => OnnxProvider.FromDirectory(Path.Combine(Dir, "nope")));
    }

    [Fact]
    public void No_GRAPH_names_both_layouts_it_looked_for()
    {
        var error = Assert.Throws<FileNotFoundException>(() => OnnxProvider.FromDirectory(Dir));

        Assert.Contains("onnx/model.onnx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_EXPLICIT_model_file_that_is_absent_names_that_file_not_the_default()
    {
        var error = Assert.Throws<FileNotFoundException>(
            () => OnnxProvider.FromDirectory(Dir, new OnnxProviderOptions { ModelFile = "onnx/model_qint8.onnx" }));

        Assert.Contains("model_qint8.onnx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_graph_WITHOUT_a_vocabulary_fails_on_the_vocabulary()
    {
        // Ordering matters: the graph is found, so the next missing piece must be named rather than
        // surfacing as a null reference inside the tokenizer.
        Directory.CreateDirectory(Path.Combine(Dir, "onnx"));
        File.WriteAllText(Path.Combine(Dir, "onnx", "model.onnx"), "not really a graph");

        var error = Assert.Throws<FileNotFoundException>(() => OnnxProvider.FromDirectory(Dir));

        Assert.Contains("vocab.txt", error.Message, StringComparison.Ordinal);
    }
}

/// <summary>The ONNX vector backend against a REAL export, which nothing above can stand in for: everything else
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

    private const string ReferenceModel = "all-MiniLM-L6-v2";

    /// <summary>Whether the export IS the model the pinned figures belong to — named by
    /// <c>LYNTAI_ONNX_MODEL_ID</c>, else by the export's directory, the name it is downloaded under. Width is
    /// not identity: bge-small, e5-small and paraphrase-MiniLM are 384-wide too, and fail these figures. Nor is
    /// <c>config.json</c>'s <c>_name_or_path</c>, which on this export names the BASE model it was tuned from.</summary>
    private static bool IsTheReferenceModel()
    {
        var id = Environment.GetEnvironmentVariable("LYNTAI_ONNX_MODEL_ID") is { Length: > 0 } named
            ? named
            : Path.GetFileName(ModelDirectory?.TrimEnd('/', '\\'));
        return id?.Contains(ReferenceModel, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static void SkipUnlessTheReferenceModel() =>
        Skip.IfNot(IsTheReferenceModel(),
            $"these figures are {ReferenceModel}'s; set LYNTAI_ONNX_MODEL_ID if this export is it under another name");

    private static OnnxProvider Load(InputSegmentation? segmentation = null)
    {
        Skip.If(string.IsNullOrWhiteSpace(ModelDirectory), "set LYNTAI_ONNX_MODEL_DIR to an ONNX export");
        return OnnxProvider.FromDirectory(ModelDirectory!, new OnnxProviderOptions { Segmentation = segmentation });
    }

    /// <summary>Two inputs sharing their first 1,200 tokens, differing only past a 512-token window.</summary>
    private static string[] SharedHead()
    {
        var shared = string.Join(' ', Enumerable.Repeat("alpha beta gamma", 400));
        return [$"{shared} the weather forecast for tomorrow", $"{shared} a stock market share price quote", "short"];
    }

    [SkippableFact]
    public async Task A_real_model_ranks_a_related_pair_above_an_unrelated_one()
    {
        using var vectorProvider = Load();

        var vectors = await vectorProvider.EmbedAsync([
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
        using var vectorProvider = Load();
        var vectors = await vectorProvider.EmbedAsync([
            "the weather forecast for tomorrow",
            "a stock market share price quote",
            "tomorrow's weather forecast",
        ]);

        // Pinned against all-MiniLM-L6-v2 specifically, so a different export skips rather than failing on
        // numbers that were never about it.
        SkipUnlessTheReferenceModel();

        Assert.Equal(0.979984, Cosine(vectors[0], vectors[2]), 4);
        Assert.Equal(0.146295, Cosine(vectors[0], vectors[1]), 4);
    }

    [SkippableFact]
    public async Task Takes_NORMALIZATION_from_the_models_own_modules_declaration()
    {
        // all-MiniLM-L6-v2 lists a Normalize module, so its vectors are unit length without anyone asking.
        // Reading it wrong is invisible to cosine, which is scale-invariant — so nothing downstream would
        // report it, which is exactly why it is asserted here.
        using var vectorProvider = Load();
        SkipUnlessTheReferenceModel();

        var vector = (await vectorProvider.EmbedAsync(["the weather forecast for tomorrow"]))[0];

        Assert.Equal(1.0, Math.Sqrt(vector.Sum(v => (double)v * v)), 4);
    }

    [SkippableFact]
    public async Task TRUNCATES_past_its_context_limit_by_DEFAULT_rather_than_throwing()
    {
        // A transformer has positional embeddings, so by default an over-long input is cut at the window:
        // two inputs that differ only past it embed IDENTICALLY. Failing instead would mean a single long
        // document could refuse a whole corpus.
        using var vectorProvider = Load();

        var vectors = await vectorProvider.EmbedAsync(SharedHead());

        Assert.Equal(vectors[0], vectors[1]);
        Assert.All(vectors, v => Assert.Contains(v, component => component != 0f));
    }

    [SkippableFact]
    public async Task With_SEGMENTATION_the_TAIL_past_the_context_limit_still_counts()
    {
        // D177: segmented, the input runs as windows pooled into one vector, so the tail moves it
        using var vectorProvider = Load(new InputSegmentation());

        var vectors = await vectorProvider.EmbedAsync(SharedHead());

        Assert.NotEqual(vectors[0], vectors[1]);
        Assert.Equal(vectors[0].Length, vectors[2].Length);
        if (IsTheReferenceModel())   // unit length is the reference model's declaration, not every export's
            Assert.All(vectors, v => Assert.Equal(1.0, Math.Sqrt(v.Sum(c => (double)c * c)), 4));
    }

    [SkippableFact]
    public async Task Reports_an_id_and_availability_like_every_other_provider()
    {
        using var vectorProvider = Load();

        Assert.Equal("onnx", vectorProvider.Id);
        Assert.True(vectorProvider.IsAvailable);
        Assert.True(vectorProvider.MaxTokens > 0);
        if (IsTheReferenceModel()) Assert.Equal(512, vectorProvider.MaxTokens);
        Assert.NotEmpty((await vectorProvider.EmbedAsync(["x"]))[0]);
    }

}
