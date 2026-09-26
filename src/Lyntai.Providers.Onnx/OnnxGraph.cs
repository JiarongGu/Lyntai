using Lyntai.Text;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lyntai.Providers.Onnx;

/// <summary>What every backend in this package shares: finding the graph a download actually shipped, and
/// feeding it the tensors a BERT-family model takes.
///
/// <para>Shared rather than copied because both halves are silent when wrong — a second layout probe drifts
/// from the first, and an omitted <c>token_type_ids</c> costs a cross-encoder the signal that tells its
/// query from its document.</para></summary>
internal static class OnnxGraph
{
    /// <summary>The graph, in the two layouts a downloaded export actually uses.</summary>
    /// <param name="directory">The model directory.</param>
    /// <param name="modelFile">An explicit file, relative to it, or null to probe.</param>
    /// <param name="setting">The option a caller would set to name one — each backend has its own.</param>
    /// <exception cref="FileNotFoundException">No graph, naming what was looked for.</exception>
    public static string Resolve(string directory, string? modelFile, string setting)
    {
        if (!string.IsNullOrWhiteSpace(modelFile))
        {
            var chosen = Path.Combine(directory, modelFile);
            return File.Exists(chosen)
                ? chosen
                : throw new FileNotFoundException($"'{modelFile}' is missing from '{directory}'.", chosen);
        }

        foreach (var candidate in new[] { Path.Combine("onnx", "model.onnx"), "model.onnx" })
        {
            var probed = Path.Combine(directory, candidate);
            if (File.Exists(probed)) return probed;
        }

        throw new FileNotFoundException(
            $"No ONNX graph in '{directory}' — looked for onnx/model.onnx and model.onnx. Set "
            + $"{setting} to name one directly.",
            Path.Combine(directory, "onnx", "model.onnx"));
    }

    /// <summary>The batch's inputs, each padded to <paramref name="width"/>. Emitted only for the tensors the
    /// graph DECLARES — a distilled export may drop <c>token_type_ids</c>, and passing an input it does not
    /// declare is an error rather than a no-op.</summary>
    public static List<NamedOnnxValue> Feed(
        InferenceSession session, TokenEncoding[] encodings, int width)
    {
        var inputs = new List<NamedOnnxValue>(TensorSources.Length);
        foreach (var (name, select) in TensorSources)
        {
            if (!session.InputMetadata.ContainsKey(name)) continue;
            inputs.Add(NamedOnnxValue.CreateFromTensor(name, Pad(encodings, select, width)));
        }

        return inputs;
    }

    /// <summary>The three tensors a BERT graph takes, and how to read each from an encoding.</summary>
    private static readonly (string Name, Func<TokenEncoding, int[]> Select)[] TensorSources =
    [
        ("input_ids", e => e.Ids),
        ("attention_mask", e => e.AttentionMask),
        ("token_type_ids", e => e.TokenTypeIds),
    ];

    /// <summary>Zero-padded to the widest row. Padding is masked out of attention and pooling, so its id is
    /// inert — and with the window capped at the real position limit, a RoBERTa graph's padding-derived position
    /// ids stay inside its table. Mask 0 excludes the row, and segment 0 is what padding belongs to.</summary>
    private static DenseTensor<long> Pad(
        TokenEncoding[] encodings, Func<TokenEncoding, int[]> select, int width)
    {
        var tensor = new DenseTensor<long>([encodings.Length, width]);
        for (var i = 0; i < encodings.Length; i++)
        {
            var values = select(encodings[i]);
            for (var t = 0; t < values.Length; t++) tensor[i, t] = values[t];
        }

        return tensor;
    }
}
