namespace Lyntai.Providers.Onnx;

/// <summary>Knobs for <see cref="OnnxCrossEncoder"/>. <b>Separate from
/// <see cref="OnnxProviderOptions"/> rather than shared with it</b>: pooling and normalization are an
/// vector backend's questions, and a cross-encoder has no answer to either — its head emits the score
/// directly.</summary>
public sealed class OnnxCrossEncoderOptions
{
    /// <summary>The provider id this backend reports as
    /// <see cref="Lyntai.Inference.IModelProvider.Id"/>. It is a LABEL for one configured backend, so two
    /// rerankers loaded at once are two ids.</summary>
    public string Id { get; set; } = "onnx-rerank";

    /// <summary>Maximum sequence length for the WHOLE pair, including all three special tokens. Null reads
    /// <c>config.json</c>'s <c>max_position_embeddings</c>, defaulting to 512.
    /// <para><b>The budget is spent on the DOCUMENT</b>: an over-long pair is truncated from the document
    /// end, so the query is shortened only when it cannot fit on its own. Losing a document's tail costs
    /// some evidence; losing the query's changes the question being asked.</para></summary>
    public int? MaxTokens { get; set; }

    /// <summary>The model file, relative to the directory. Null probes <c>onnx/model.onnx</c> then
    /// <c>model.onnx</c> — the two layouts a downloaded export actually uses. Set it to pick a quantized
    /// sibling such as <c>onnx/model_qint8_avx512_vnni.onnx</c>, which for the measured reference model is
    /// 23,200,716 B against the fp32 graph's 91,011,230 B.</summary>
    public string? ModelFile { get; set; }
}
