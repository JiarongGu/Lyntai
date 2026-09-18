using Lyntai.Inference;
using Lyntai.Text;
using Microsoft.ML.OnnxRuntime;

namespace Lyntai.Providers.Onnx;

/// <summary>The BI-ENCODER dialect: one text per row, the per-token output reduced to one vector.
/// <see cref="OnnxProvider"/>'s default, and what a sentence-transformer export is.</summary>
/// <param name="pooling">How the token rows become one vector.</param>
/// <param name="normalize">Whether to L2-normalize the result, so cosine is a dot product.</param>
internal sealed class OnnxPoolingDialect(OnnxPooling pooling, bool normalize) : IOnnxVectorDialect
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
    public float[][] Embed(OnnxRun run, string outputName, IReadOnlyList<string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);

        var encodings = new WordPieceEncoding[texts.Count];
        for (var i = 0; i < texts.Count; i++)
            encodings[i] = run.Tokenizer.Encode(texts[i] ?? string.Empty, run.MaxTokens);

        var width = encodings.Max(e => e.Ids.Length);
        using var results = run.Session.Run(OnnxGraph.Feed(run.Session, encodings, width), [outputName]);
        var hidden = results[0].AsTensor<float>();
        var hiddenSize = hidden.Dimensions[2];
        var flat = hidden.ToArray();

        var vectors = new float[texts.Count][];
        for (var i = 0; i < texts.Count; i++)
        {
            var block = flat.AsSpan(i * width * hiddenSize, width * hiddenSize);
            vectors[i] = VectorPooling.Reduce(
                block, hiddenSize, PaddedMask(encodings[i], width), pooling, normalize);
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
