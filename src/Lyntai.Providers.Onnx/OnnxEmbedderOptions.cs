namespace Lyntai.Providers.Onnx;

/// <summary>How a transformer's per-token output is reduced to one vector per text.</summary>
public enum OnnxPooling
{
    /// <summary>Average the attended tokens. The sentence-transformers default and what most
    /// <c>all-*</c> and <c>*-MiniLM-*</c> exports are trained for.</summary>
    Mean = 0,

    /// <summary>Take the classification token's row alone. What the BGE family is trained for.</summary>
    Cls = 1,
}

/// <summary>Knobs for <see cref="OnnxEmbedder"/>. <b>Every one defaults to reading the model</b>, because
/// pooling, normalization and the position limit are properties of how it was trained rather than caller
/// preferences — set one only to override a model that ships a wrong or missing declaration.</summary>
public sealed class OnnxEmbedderOptions
{
    /// <summary>The provider id this backend reports as
    /// <see cref="Lyntai.Lifecycle.IModelProvider.Id"/>.</summary>
    public string Id { get; set; } = "onnx";

    /// <summary>Pooling mode. Null reads <c>1_Pooling/config.json</c>, defaulting to
    /// <see cref="OnnxPooling.Mean"/> when the model ships none.</summary>
    public OnnxPooling? Pooling { get; set; }

    /// <summary>Whether to L2-normalize each vector. Null reads <c>modules.json</c> for a <c>Normalize</c>
    /// module. <b>It does not change cosine RANKING</b>, which is scale-invariant — it matters when a
    /// consumer compares by dot product or stores vectors for something that assumes unit length.</summary>
    public bool? Normalize { get; set; }

    /// <summary>Maximum sequence length INCLUDING <c>[CLS]</c> and <c>[SEP]</c>. Null reads
    /// <c>config.json</c>'s <c>max_position_embeddings</c>, defaulting to 512.
    /// <para><b>Longer text is TRUNCATED, not refused</b>, which is what every BERT pipeline does — and the
    /// one capability a <c>model2vec</c> table has over this class is having no such limit at all.</para></summary>
    public int? MaxTokens { get; set; }

    /// <summary>The model file, relative to the directory. Null probes <c>onnx/model.onnx</c> then
    /// <c>model.onnx</c> — the two layouts a downloaded export actually uses. Set it to pick a quantized
    /// sibling such as <c>onnx/model_qint8_avx512_vnni.onnx</c>.</summary>
    public string? ModelFile { get; set; }
}
