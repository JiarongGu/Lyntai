using Lyntai.Inference;
using Lyntai.Text;
using Microsoft.ML.OnnxRuntime;

namespace Lyntai.Providers.Onnx;

/// <summary>The CROSS-ENCODER head: a query/document pair per row in the model's own layout — BERT's
/// <c>[CLS] q [SEP] d [SEP]</c>, XLM-R's <c>&lt;s&gt; q &lt;/s&gt;&lt;/s&gt; d &lt;/s&gt;</c> — a row per window
/// of a segmented document — the classification head read as one score per pair. Selected by
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
    public double[] Score(OnnxRun run, string outputName, string query, IReadOnlyList<string> documents,
        int? maxPiecesPerInput) =>
        Score(run.Windows, query, documents, rows =>
        {
            var width = rows.Max(e => e.Ids.Length);
            using var results = run.Session.Run(OnnxGraph.Feed(run.Session, rows, width), [outputName]);
            var head = results[0].AsTensor<float>();
            return CrossEncoderLogits.Read(head.ToArray(), head.Dimensions, rows.Length);
        }, maxPiecesPerInput);

    /// <summary>Score each document as its BEST window (<c>docs/DECISIONS.md</c> <b>D177</b>): a document is
    /// as relevant as its most relevant passage, and one that took a single row scores as that row. The rows
    /// go through <paramref name="forward"/> in the bounded passes <see cref="WindowedBatch.Forward{T}"/>
    /// makes, and the scores come back one per document, in input order.</summary>
    /// <param name="windows">The tokenizer, bounded by the model's window.</param>
    /// <param name="query">The question every document is scored against; never segmented.</param>
    /// <param name="documents">The documents, in the order their scores are returned.</param>
    /// <param name="forward">The graph: one score per row it is fed.</param>
    /// <param name="maxPiecesPerInput">The request's own piece cap, narrowing the record's.</param>
    internal static double[] Score(WindowedTokenizer windows, string query, IReadOnlyList<string> documents,
        Func<TokenEncoding[], double[]> forward, int? maxPiecesPerInput = null)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var batch = windows.EncodePairs(query, documents, maxPiecesPerInput);
        var rowScores = batch.Forward(forward);
        return [.. Enumerable.Range(0, documents.Count)
            .Select(i => rowScores[batch.First[i]..batch.First[i + 1]].Max())];
    }
}
