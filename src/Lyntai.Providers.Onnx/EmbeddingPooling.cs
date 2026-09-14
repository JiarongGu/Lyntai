using Lyntai.Memory;

namespace Lyntai.Providers.Onnx;

/// <summary>Reduces a transformer's per-token output to one vector.
///
/// <para>Separated from the session on purpose: this is the half that can be WRONG without failing — a mean
/// that includes padding, or a normalize that divides by zero — and it is the half a test can reach without
/// a 90 MB model on disk.</para></summary>
internal static class EmbeddingPooling
{
    /// <summary>Pool <paramref name="tokens"/> — a row-major <c>[token, width]</c> block for ONE text.</summary>
    /// <param name="tokens">The model's last hidden state for this text.</param>
    /// <param name="width">Vector width (the model's hidden size).</param>
    /// <param name="mask">1 per real token, 0 per padding. Must be one entry per token row.</param>
    /// <param name="pooling">Mean over attended tokens, or the first row.</param>
    /// <param name="normalize">L2-normalize the result.</param>
    public static float[] Reduce(
        ReadOnlySpan<float> tokens, int width, ReadOnlySpan<int> mask, OnnxPooling pooling, bool normalize)
    {
        var vector = pooling == OnnxPooling.Cls ? Cls(tokens, width) : Mean(tokens, width, mask);
        if (normalize) VectorMath.NormalizeInPlace(vector);
        return vector;
    }

    /// <summary><c>[CLS]</c> is row zero by construction — <c>Encode</c> puts it there.</summary>
    private static float[] Cls(ReadOnlySpan<float> tokens, int width) => tokens[..width].ToArray();

    /// <summary>Mean over ATTENDED rows only. <b>Including padding is the silent failure this guards</b>:
    /// padding rows are real numbers, not zeros, so a naive mean shifts every vector by an amount that
    /// depends on how long the longest text in the batch happened to be.</summary>
    private static float[] Mean(ReadOnlySpan<float> tokens, int width, ReadOnlySpan<int> mask)
    {
        var vector = new float[width];
        var counted = 0;
        for (var t = 0; t < mask.Length; t++)
        {
            if (mask[t] == 0) continue;
            var row = tokens.Slice(t * width, width);
            for (var i = 0; i < width; i++) vector[i] += row[i];
            counted++;
        }

        // A fully-masked text cannot happen through Encode, which always emits [CLS] and [SEP] — but
        // dividing by zero here would yield NaN, which compares false against everything and poisons a
        // store silently rather than failing.
        if (counted == 0) return vector;
        for (var i = 0; i < width; i++) vector[i] /= counted;
        return vector;
    }

}
