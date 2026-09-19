using Lyntai.Inference;
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

/// <summary>Knobs for <see cref="OnnxProvider"/>. <b>Every one defaults to reading the model</b>, because
/// pooling, normalization and the position limit are properties of how it was trained rather than caller
/// preferences — set one only to override a model that ships a wrong or missing declaration.</summary>
public sealed class OnnxProviderOptions
{
    /// <summary>The provider id this backend reports as
    /// <see cref="Lyntai.Inference.IModelProvider.Id"/>.</summary>
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
    /// <para><b>Longer text is TRUNCATED, not refused</b>, which is what every BERT-family encoder does — and the
    /// one capability a <c>model2vec</c> table has over this class is having no such limit at all.</para></summary>
    public int? MaxTokens { get; set; }

    /// <summary>The model file, relative to the directory. Null probes <c>onnx/model.onnx</c> then
    /// <c>model.onnx</c> — the two layouts a downloaded export actually uses. Set it to pick a quantized
    /// sibling such as <c>onnx/model_qint8_avx512_vnni.onnx</c>.</summary>
    public string? ModelFile { get; set; }

    /// <summary>What this backend puts out — <see cref="Lyntai.Inference.ProviderKinds.Vector"/> (default)
    /// embeds with the model's own pooling, <see cref="Lyntai.Inference.ProviderKinds.Score"/> runs it as a
    /// cross-encoder over <c>(query, document)</c> pairs. It is the field that decides how a call is
    /// encoded, which graph output is read, and which methods the provider answers — the same field, doing
    /// the same job, as <c>HttpModelOptions.Produces</c> one package over.
    ///
    /// <para><b>The BACKEND is the same either way</b> (<c>docs/DECISIONS.md</c> <b>D157</b>): one ONNX
    /// session, one tokenizer, one forward pass. Only the weights on disk and the two ends of the call
    /// differ, which is why this is a field rather than a second registration or a second class.</para>
    ///
    /// <para>A single value rather than a list, because one registration is one backend. <b>One model is
    /// one graph</b>, so a deployment wanting both an embedder and a reranker calls <c>AddOnnxProvider</c>
    /// twice, with distinct <see cref="Id"/> values and distinct model directories.</para>
    ///
    /// <para><see cref="Pooling"/> and <see cref="Normalize"/> configure the vector path and are ignored by
    /// any other.</para></summary>
    public string Produces { get; set; } = ProviderKinds.Vector;
}
