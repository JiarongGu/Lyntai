namespace Lyntai.Memory;

/// <summary>Shared vector math for <see cref="IVectorStore"/> backends, so brute-force stores rank
/// identically (the InMemory default and the SQLite store both use this; a BYO backend can too).</summary>
public static class VectorMath
{
    /// <summary>Cosine similarity of two equal-length vectors — dot / (‖a‖·‖b‖); 0 when either is a zero
    /// vector, and 0 on a DIMENSION MISMATCH (a stored vector from a different embedding model — a stray
    /// wrong-dim row ranks last rather than throwing and sinking the whole search). Keeping this in ONE
    /// place is what makes <see cref="IVectorStore"/> behave consistently across backends.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    /// <summary>Scale <paramref name="vector"/> to unit length, IN PLACE. A zero vector is left alone.
    ///
    /// <para><b>Here because every vector backend wants it and none of them can share it otherwise.</b>
    /// L2-normalization is runtime-independent arithmetic — the ONNX adapter and the model2vec one each
    /// carried a copy, in different packages, so Core is the only place either can reach. A BYO
    /// backend that normalizes wants the same one, for the same reason this type exists at all:
    /// two backends that normalize differently do not rank identically.</para>
    ///
    /// <para><b>The zero guard is load-bearing.</b> Dividing by a zero length yields NaN in every component,
    /// and NaN compares false against everything — so the vector is stored, never matches, and reports no
    /// error. Accumulating in <c>double</c> before the square root is the same care: a 1024-wide float sum
    /// of squares loses precision that shifts the unit vector.</para></summary>
    /// <param name="vector">Modified in place.</param>
    public static void NormalizeInPlace(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        double sum = 0;
        foreach (var v in vector) sum += (double)v * v;
        var length = Math.Sqrt(sum);
        if (length <= 0) return;
        for (var i = 0; i < vector.Length; i++) vector[i] = (float)(vector[i] / length);
    }

    /// <summary>The weighted mean DIRECTION of several vectors: each is scaled to unit length, the unit
    /// vectors are summed by weight, and the sum is re-normalised — one unit vector standing for several,
    /// such as the pieces of a text too long to embed in one window.
    ///
    /// <para>A zero vector has no direction and contributes nothing, whatever its weight. Where the weighted
    /// sum is itself zero — every vector zero, or directions that cancel — the result is the unit vector of
    /// the HEAVIEST vector (the first on a tie), and a zero vector when that one is zero too; never NaN.</para>
    ///
    /// <para>Sums in <c>double</c>, as <see cref="NormalizeInPlace"/> does.</para></summary>
    /// <param name="vectors">The vectors, all of one dimension. Not modified.</param>
    /// <param name="weights">One weight per vector, non-negative and finite — a piece's length, say.</param>
    /// <returns>A new array of the same dimension: unit length, or zero when every vector is zero.</returns>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="ArgumentException">No vectors, a weight count that differs from the vector count,
    /// or vectors of different dimensions.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A weight is negative, infinite or NaN.</exception>
    public static float[] WeightedMeanDirection(IReadOnlyList<float[]> vectors, IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(weights);
        if (vectors.Count == 0) throw new ArgumentException("There is nothing to pool.", nameof(vectors));
        if (weights.Count != vectors.Count)
            throw new ArgumentException(
                $"{weights.Count} weights were given for {vectors.Count} vectors.", nameof(weights));

        var dimension = vectors[0].Length;
        var sum = new double[dimension];
        var heaviest = 0;
        for (var j = 0; j < vectors.Count; j++)
        {
            var (vector, weight) = (vectors[j], weights[j]);
            if (vector.Length != dimension)
                throw new ArgumentException(
                    $"Vector {j} has {vector.Length} dimensions where the first has {dimension}.", nameof(vectors));
            if (!double.IsFinite(weight) || weight < 0)
                throw new ArgumentOutOfRangeException(
                    nameof(weights), weight, $"Weight {j} is not a finite, non-negative number.");

            if (weight > weights[heaviest]) heaviest = j;
            var length = Length(vector);
            if (length == 0) continue;
            for (var k = 0; k < dimension; k++) sum[k] += weight * vector[k] / length;
        }

        var total = Math.Sqrt(sum.Sum(x => x * x));
        if (total > 0) return [.. sum.Select(x => (float)(x / total))];

        var fallback = (float[])vectors[heaviest].Clone();
        NormalizeInPlace(fallback);
        return fallback;
    }

    private static double Length(float[] vector)
    {
        double sum = 0;
        foreach (var v in vector) sum += (double)v * v;
        return Math.Sqrt(sum);
    }
}
