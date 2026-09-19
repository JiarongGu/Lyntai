using Lyntai.Inference;
using Lyntai.Text;
using Microsoft.ML.OnnxRuntime;

namespace Lyntai.Providers.Onnx;

/// <summary>The CROSS-ENCODER head: a <c>[CLS] query [SEP] document [SEP]</c> pair per row, the
/// classification head read as one score per pair. Selected by
/// <see cref="OnnxProviderOptions.Produces"/>, so the same <see cref="OnnxProvider"/> serves
/// <see cref="ProviderKinds.Score"/> instead of vectors — the model on disk is what differs, not the
/// backend (<c>docs/DECISIONS.md</c> <b>D157</b>).</summary>
internal sealed class OnnxCrossEncoderHead : IOnnxScoreHead
{
    /// <inheritdoc />
    public string Produces => ProviderKinds.Score;

    /// <summary>The classification head's output, refused at COMPOSITION when the graph's own declaration
    /// says it cannot carry one score per pair — a multi-label (NLI) head above all, which otherwise loads,
    /// scores, and ranks backwards.
    ///
    /// <para>A graph that declares a DYNAMIC label axis says too little to refuse on, and is judged by
    /// <see cref="CrossEncoderLogits.Read"/> instead, against the tensor it actually returns.</para></summary>
    public string ResolveOutput(InferenceSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return CrossEncoderLogits.ScoreOutput(
            [.. session.OutputMetadata.Keys], name => session.OutputMetadata[name].Dimensions);
    }

    /// <inheritdoc />
    /// <remarks><b>The segment ids are the point.</b> They are what tells the model which half is the
    /// question, and a runtime that zeroes them scores the pair as one undifferentiated string — which is
    /// how the same weights rank a published reference pair BACKWARDS through llama.cpp
    /// (<c>docs/memory-measurements.md</c> §5).</remarks>
    public double[] Score(OnnxRun run, string outputName, string query, IReadOnlyList<string> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);

        var encodings = new WordPieceEncoding[documents.Count];
        for (var i = 0; i < documents.Count; i++)
            encodings[i] = run.Tokenizer.Encode(query, documents[i] ?? string.Empty, run.MaxTokens);

        var width = encodings.Max(e => e.Ids.Length);
        using var results = run.Session.Run(OnnxGraph.Feed(run.Session, encodings, width), [outputName]);
        var head = results[0].AsTensor<float>();
        return CrossEncoderLogits.Read(head.ToArray(), head.Dimensions, documents.Count);
    }
}
