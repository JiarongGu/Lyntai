namespace Lyntai.Providers.Onnx;

/// <summary>Reads one relevance score per pair out of a classification head's output.
///
/// <para>Separated from the session for the reason <see cref="EmbeddingPooling"/> is: this is the half that
/// can be WRONG without failing — a head with more than one label still returns finite, well-ordered-looking
/// numbers — and the half a test can reach without a model on disk.</para></summary>
internal static class CrossEncoderLogits
{
    /// <summary>The scores, in the order the pairs were sent.</summary>
    /// <param name="logits">The head's output, row-major.</param>
    /// <param name="dimensions">Its shape: <c>[pairs, 1]</c>, or <c>[pairs]</c> where an exporter squeezed
    /// the label axis away.</param>
    /// <param name="rows">How many pairs were sent.</param>
    /// <exception cref="InvalidOperationException">The head is not a single-logit one, or it scored a
    /// different number of pairs than were sent. It THROWS rather than returning a degraded answer for the
    /// reason <c>IModelProvider.ScoreAsync</c> states: there is no score meaning "I could not", and a zero
    /// ranks as confidently as any other number.</exception>
    public static double[] Read(ReadOnlySpan<float> logits, ReadOnlySpan<int> dimensions, int rows)
    {
        var labels = dimensions.Length switch
        {
            1 => 1,
            2 => dimensions[1],
            _ => throw new InvalidOperationException(
                $"A cross-encoder emits ONE logit per pair, shaped [pairs] or [pairs, 1]; this output has "
                + $"{dimensions.Length} dimensions. A per-token output — what a bi-encoder emits — has three."),
        };

        // Relevance is not necessarily the FIRST of several labels, so reading a column out of an
        // NLI-shaped head would return well-formed numbers in the wrong order. That is the exact failure
        // this package exists to have escaped, so it is refused rather than guessed at.
        if (labels != 1)
            throw new InvalidOperationException(
                $"A cross-encoder emits ONE logit per pair; this head has {labels} labels, and which of them "
                + "means relevance is the model's own convention rather than something to assume.");

        // Pairing by position is only sound when the counts agree; a short answer scores the wrong documents.
        if (dimensions[0] != rows || logits.Length < rows)
            throw new InvalidOperationException(
                $"The head scored {dimensions[0]} pairs of the {rows} it was sent.");

        var scores = new double[rows];
        for (var i = 0; i < rows; i++) scores[i] = logits[i];
        return scores;
    }
}
