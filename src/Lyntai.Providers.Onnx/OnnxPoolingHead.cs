using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Text;
using Microsoft.ML.OnnxRuntime;

namespace Lyntai.Providers.Onnx;

/// <summary>The BI-ENCODER head: one text per row, the per-token output reduced to one vector.
/// <see cref="OnnxProvider"/>'s default, and what a sentence-transformer export is.</summary>
/// <param name="pooling">How the token rows become one vector.</param>
/// <param name="normalize">Whether to L2-normalize the result, so cosine is a dot product.</param>
internal sealed class OnnxPoolingHead(OnnxPooling pooling, bool normalize) : IOnnxVectorHead
{
    /// <inheritdoc />
    public string Produces => ProviderKinds.Vector;

    /// <summary>The per-token output, by name where the export gives one and by position otherwise.
    /// <b>Never the pooled output</b>: some exports add a <c>sentence_embedding</c>, but taking it would
    /// silently ignore the configured pooling and return whatever the exporter chose.</summary>
    public string ResolveOutput(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        foreach (var candidate in new[] { "last_hidden_state", "token_embeddings" })
            if (session.OutputMetadata.ContainsKey(candidate)) return candidate;
        return session.OutputMetadata.Keys.First();
    }

    /// <inheritdoc />
    public float[][] Embed(OnnxRun run, string outputName, IReadOnlyList<string> texts) =>
        Embed(run.Windows, texts, rows => Reduce(run.Session, outputName, rows));

    /// <summary>One vector per text: a text within the window gets its row's vector exactly as
    /// <paramref name="forward"/> computed it; a longer one gets its windows' vectors pooled by
    /// <see cref="VectorMath.WeightedMeanDirection"/>, weighted by each window's tokens (<c>docs/DECISIONS.md</c>
    /// <b>D177</b>) — unit length whatever <c>normalize</c> says. Every window goes through
    /// <paramref name="forward"/> in one batch.</summary>
    /// <param name="windows">The tokenizer, bounded by the model's window.</param>
    /// <param name="texts">The texts, in the order their vectors are returned.</param>
    /// <param name="forward">The graph plus the reduction: one vector per row it is fed.</param>
    internal static float[][] Embed(WindowedTokenizer windows, IReadOnlyList<string> texts,
        Func<WordPieceEncoding[], float[][]> forward)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var batch = windows.EncodeTexts(texts);
        var rowVectors = forward(batch.Rows);

        var vectors = new float[texts.Count][];
        for (var i = 0; i < vectors.Length; i++)
        {
            var (first, end) = (batch.First[i], batch.First[i + 1]);
            vectors[i] = batch.IsSegmented(i)
                ? VectorMath.WeightedMeanDirection(
                    rowVectors[first..end], [.. batch.Weights[first..end].Select(w => (double)w)])
                : rowVectors[first];
        }

        return vectors;
    }

    /// <summary>The graph's per-token output for each row, reduced to one vector per row.</summary>
    private float[][] Reduce(InferenceSession session, string outputName, WordPieceEncoding[] rows)
    {
        var width = rows.Max(e => e.Ids.Length);
        using var results = session.Run(OnnxGraph.Feed(session, rows, width), [outputName]);
        var hidden = results[0].AsTensor<float>();
        var hiddenSize = hidden.Dimensions[2];
        var flat = hidden.ToArray();

        var vectors = new float[rows.Length][];
        for (var i = 0; i < rows.Length; i++)
        {
            var block = flat.AsSpan(i * width * hiddenSize, width * hiddenSize);
            vectors[i] = VectorPooling.Reduce(block, hiddenSize, PaddedMask(rows[i], width), pooling, normalize);
        }

        return vectors;
    }

    /// <summary>The attention mask widened to the batch, zero over the padding — which is what tells
    /// <see cref="VectorPooling"/> not to average the padded rows in.</summary>
    private static int[] PaddedMask(WordPieceEncoding encoding, int width)
    {
        var mask = new int[width];
        encoding.AttentionMask.CopyTo(mask, 0);
        return mask;
    }
}
