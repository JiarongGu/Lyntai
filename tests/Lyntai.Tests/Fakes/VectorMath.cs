namespace Lyntai.Tests.Fakes;

/// <summary>Vector arithmetic a test compares embeddings with — <c>using static</c> it.</summary>
public static class VectorMath
{
    /// <summary>Cosine similarity; 0 when either vector is all zeros.</summary>
    public static double Cosine(float[] a, float[] b)
    {
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }
}
