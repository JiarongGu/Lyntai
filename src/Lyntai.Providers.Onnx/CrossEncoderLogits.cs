namespace Lyntai.Providers.Onnx;

/// <summary>Reads one relevance score per pair out of a classification head's output.
///
/// <para>Separated from the session for the reason <see cref="VectorPooling"/> is: this is the half that
/// can be WRONG without failing — a head with more than one label still returns finite, well-ordered-looking
/// numbers — and the half a test can reach without a model on disk.</para></summary>
internal static class CrossEncoderLogits
{
    /// <summary>Why this output shape cannot carry one score per pair, or null when it can — <b>or when the
    /// graph declares too little to tell</b>, which is a deferral rather than an endorsement.
    ///
    /// <para><b>It exists so the refusal can be made where something is still listening.</b> Thrown from
    /// <see cref="Read"/> it reaches <c>ScoringVerificationPolicy</c>, which is fail-open by contract and
    /// reports <c>NoOpinion</c> — so a multi-label export arrives at a deployment as *every recall silently
    /// unverified*. <see cref="OnnxCrossEncoder.FromDirectory"/> asks the same question of
    /// <c>OutputMetadata</c> instead, where a throw still stops something.</para>
    ///
    /// <para>ONNX declares a dynamic axis as <c>-1</c>, so a graph that leaves its label count open is
    /// judged on the tensor it actually returns rather than refused for declining to say.</para></summary>
    /// <param name="dimensions">A DECLARED shape (<c>[-1, 1]</c>) or a returned one (<c>[8, 1]</c>).</param>
    public static string? ShapeProblem(ReadOnlySpan<int> dimensions)
    {
        if (dimensions.Length is not (1 or 2))
            return "A cross-encoder emits ONE logit per pair, shaped [pairs] or [pairs, 1]; this output has "
                + $"{dimensions.Length} dimensions. A per-token output — what a bi-encoder emits — has three.";

        // Relevance is not necessarily the FIRST of several labels, so reading a column out of an
        // NLI-shaped head would return well-formed numbers in the wrong order. That is the exact failure
        // this package exists to have escaped, so it is refused rather than guessed at.
        var labels = dimensions.Length == 1 ? 1 : dimensions[1];
        return labels > 1
            ? $"A cross-encoder emits ONE logit per pair; this head has {labels} labels, and which of them "
                + "means relevance is the model's own convention rather than something to assume."
            : null;
    }

    /// <summary>Which of a graph's outputs carries the score — <c>logits</c> by name where the export gives
    /// one, otherwise the single output it has — refused when its DECLARED shape cannot carry one.
    ///
    /// <para><b>Takes the declaration rather than the session</b>, so the composition-time refusal is
    /// reachable by a test at all: a check welded to a native handle is one that can be deleted without
    /// turning anything red.</para></summary>
    /// <param name="names">The graph's output names.</param>
    /// <param name="shapeOf">That output's declared shape, <c>-1</c> for an axis left open.</param>
    /// <exception cref="InvalidOperationException">Nothing here can carry a score — several outputs and no
    /// <c>logits</c> among them, or a head with more than one label.</exception>
    public static string ScoreOutput(IReadOnlyList<string> names, Func<string, int[]> shapeOf)
    {
        var name = names.Contains("logits") ? "logits"
            : names.Count == 1 ? names[0]
            : throw new InvalidOperationException(
                $"This graph has no 'logits' output and {names.Count} others to choose from "
                + $"({string.Join(", ", names)}). A bi-encoder is the likeliest cause — it embeds rather "
                + $"than scores, so load it with {nameof(OnnxProvider)} instead.");

        var shape = shapeOf(name);
        if (ShapeProblem(shape) is { } problem)
            throw new InvalidOperationException(
                $"{problem} Output '{name}' declares [{string.Join(", ", shape)}], where -1 is an axis the "
                + $"graph left open. Load a cross-encoder export, or {nameof(OnnxProvider)} if this embeds.");

        return name;
    }

    /// <summary>The scores, in the order the pairs were sent.</summary>
    /// <param name="logits">The head's output, row-major.</param>
    /// <param name="dimensions">Its shape: <c>[pairs, 1]</c>, or <c>[pairs]</c> where an exporter squeezed
    /// the label axis away.</param>
    /// <param name="rows">How many pairs were sent.</param>
    /// <exception cref="InvalidOperationException">The head is not a single-logit one
    /// (<see cref="ShapeProblem"/>), or it scored a different number of pairs than were sent. It THROWS
    /// rather than returning a degraded answer for the reason <c>IModelProvider.ScoreAsync</c> states: there
    /// is no score meaning "I could not", and a zero ranks as confidently as any other number.</exception>
    public static double[] Read(ReadOnlySpan<float> logits, ReadOnlySpan<int> dimensions, int rows)
    {
        if (ShapeProblem(dimensions) is { } problem) throw new InvalidOperationException(problem);

        // Pairing by position is only sound when the counts agree; a short answer scores the wrong documents.
        if (dimensions[0] != rows || logits.Length < rows)
            throw new InvalidOperationException(
                $"The head scored {dimensions[0]} pairs of the {rows} it was sent.");

        var scores = new double[rows];
        for (var i = 0; i < rows; i++) scores[i] = logits[i];
        return scores;
    }
}
