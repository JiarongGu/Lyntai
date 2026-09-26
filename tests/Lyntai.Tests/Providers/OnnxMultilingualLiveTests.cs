using Lyntai.Inference;
using Lyntai.Providers.Onnx;
using Lyntai.Tests.Fakes;
using static Lyntai.Tests.Fakes.VectorMath;

namespace Lyntai.Tests.Providers;

/// <summary><see cref="OnnxProvider"/> over a real SentencePiece (XLM-R) export, end to end
/// (<c>docs/DECISIONS.md</c> <b>D191</b>). Skipped without <c>LYNTAI_SPM_MODEL_DIR</c>, or without
/// <c>onnx/model.onnx</c> in it.</summary>
public class OnnxMultilingualLiveTests
{
    private const string ReferenceModel = "paraphrase-multilingual-MiniLM-L12-v2";

    private static readonly string[] Sentences =
        ["The weather is lovely today.", "Das Wetter ist heute schön.", "今天天气很好。", "The stock market fell sharply."];

    private static OnnxProvider Load(InputSegmentation? segmentation = null)
    {
        var directory = Environment.GetEnvironmentVariable("LYNTAI_SPM_MODEL_DIR");
        Skip.If(string.IsNullOrWhiteSpace(directory), "set LYNTAI_SPM_MODEL_DIR to an XLM-R sentence-transformers export");
        Skip.IfNot(File.Exists(Path.Combine(directory!, "onnx", "model.onnx")), $"no onnx/model.onnx in {directory}");
        return OnnxProvider.FromDirectory(directory!, new OnnxProviderOptions { Segmentation = segmentation });
    }

    private static bool IsTheReferenceModel() =>
        Path.GetFileName(Environment.GetEnvironmentVariable("LYNTAI_SPM_MODEL_DIR")?.TrimEnd('/', '\\'))
            ?.Contains(ReferenceModel, StringComparison.OrdinalIgnoreCase) == true;

    [SkippableFact]
    public async Task Translations_embed_nearer_than_an_unrelated_sentence()
    {
        using var provider = Load();

        var v = await provider.EmbedAsync(Sentences);

        Assert.True(Cosine(v[0], v[1]) > Cosine(v[0], v[3]), "the German translation should outrank the unrelated sentence");
        Assert.True(Cosine(v[0], v[2]) > Cosine(v[0], v[3]), "the Chinese translation should outrank the unrelated sentence");
    }

    /// <summary>Agreement with the same export through Python's <c>onnxruntime</c> and HF <c>tokenizers</c>
    /// (<c>devtools/onnx/embed-crosscheck.py</c>) — the check a wrong id, pooling or mask cannot pass.
    ///
    /// <para><b>The fp32 graph only</b> — the repository's own <c>onnx/model.onnx</c>. Its int8 siblings quantize
    /// activations over the whole batch, padding included, so the same pair moves in the second decimal between
    /// runtime versions and batch compositions; these figures are identical under onnxruntime 1.27 and 1.30.</para></summary>
    [SkippableFact]
    public async Task Agrees_with_the_PYTHON_reference_pipeline_to_four_decimals()
    {
        using var provider = Load();
        Skip.IfNot(IsTheReferenceModel(), $"these figures are {ReferenceModel}'s fp32 graph");

        var v = await provider.EmbedAsync(Sentences);

        Assert.Equal(0.972181, Cosine(v[0], v[1]), 4);
        Assert.Equal(0.885348, Cosine(v[0], v[2]), 4);
        Assert.Equal(-0.108813, Cosine(v[0], v[3]), 4);
    }

    [SkippableFact]
    public async Task A_long_multilingual_input_embeds_in_windows_within_the_position_table()
    {
        using var provider = Load(new InputSegmentation());
        var paragraph = string.Join(' ', Enumerable.Repeat("今天天气很好。Das Wetter ist heute schön. The river runs north.", 80));

        var v = await provider.EmbedAsync([paragraph, Sentences[0], Sentences[3]]);

        Assert.Equal(512, provider.MaxTokens);
        Assert.Equal(1.0, Math.Sqrt(v[0].Sum(x => (double)x * x)), 3);   // segmented: a re-normalised mean
        Assert.True(Cosine(v[0], v[1]) > Cosine(v[0], v[2]),
            $"the weather paragraph should sit nearer the weather sentence ({Cosine(v[0], v[1]):F3}) than the "
            + $"stock-market one ({Cosine(v[0], v[2]):F3})");
    }
}
