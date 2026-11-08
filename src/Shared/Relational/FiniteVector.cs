namespace Lyntai.Storage.Relational;

/// <summary>The one refusal both relational vector stores make before they touch the database; the in-process
/// store makes the same one beside its own upsert, since Core's internals do not reach an adapter.</summary>
internal static class FiniteVector
{
    /// <summary>Throws <see cref="ArgumentException"/> for a vector with a NaN or infinite component.</summary>
    public static void Require(float[] vector)
    {
        ArgumentNullException.ThrowIfNull(vector);
        foreach (var v in vector)
            if (!float.IsFinite(v))
                throw new ArgumentException("a vector component must be finite: NaN and infinity are refused", nameof(vector));
    }
}
