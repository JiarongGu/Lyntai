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
    /// <para><b>Here because every embedder backend wants it and none of them can share it otherwise.</b>
    /// L2-normalization is runtime-independent arithmetic — the ONNX adapter and the model2vec one each
    /// carried a copy, in different packages, so Core is the only place either can reach. A BYO
    /// <c>IEmbedder</c> that normalizes wants the same one, for the same reason this type exists at all:
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
}
