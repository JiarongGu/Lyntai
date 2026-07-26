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
}
