namespace Lyntai.Lifecycle;

/// <summary>Text in, one vector per text out.</summary>
/// <param name="Texts">What to embed. Order is the contract: the response's vectors come back in this
/// order, because the caller holds the texts and an index it did not send is unusable.</param>
/// <param name="Role">Which side of a retrieval this text is. Asymmetric models — E5, BGE, nomic, Arctic —
/// are trained with a distinct instruction per side and score materially worse when both sides are embedded
/// identically; a symmetric model ignores it.</param>
public sealed record VectorRequest(IReadOnlyList<string> Texts, EmbeddingRole Role = EmbeddingRole.Document);

/// <summary>The outcome of an embed call.
///
/// <para><b>The verdict sits BESIDE the vectors rather than inside them</b>, which is the whole reason this
/// type exists. There is no vector that means "I could not" — a zero compares as real and would poison a
/// store — so failure is reported on its own axis, leaving <see cref="Vectors"/> empty. That is what lets
/// embedding be routed like every other kind (<c>docs/DECISIONS.md</c> <b>D153</b>) instead of falling over
/// through exceptions.</para></summary>
/// <param name="Verdict">How the call ended.</param>
/// <param name="Vectors">One per input text, in input order. Empty unless <paramref name="Verdict"/> is
/// <see cref="ProviderVerdict.Ok"/>.</param>
/// <param name="Detail">The backend's own words, or the failure reason.</param>
public sealed record VectorResponse(
    ProviderVerdict Verdict,
    IReadOnlyList<float[]> Vectors,
    string? Detail = null) : IProviderOutcome
{
    /// <summary>Whether the call produced vectors.</summary>
    public bool IsOk => Verdict == ProviderVerdict.Ok;

    /// <summary>A successful response. <b>Throws for an EMPTY vector list</b>: an "Ok" carrying nothing is
    /// the empty-Ok mistake the LLM side already paid for — it robs routing of its chance to fall over and
    /// hands the caller a successful nothing.</summary>
    public static VectorResponse Success(IReadOnlyList<float[]> vectors, string? detail = null)
    {
        ArgumentNullException.ThrowIfNull(vectors);
        if (vectors.Count == 0)
            throw new ArgumentException("a successful embed needs at least one vector", nameof(vectors));
        return new VectorResponse(ProviderVerdict.Ok, vectors, detail);
    }

    /// <summary>A failed response, carrying no vectors.</summary>
    public static VectorResponse Failure(ProviderVerdict verdict, string? detail = null) =>
        new(verdict, [], detail);
}

/// <summary>A backend that turns text into vectors.
///
/// <para>Declared by implementing this, not by a name: the type test is what a router selects on, and
/// <see cref="ProviderCapabilities.Produces"/> states the same fact for the composition-time questions that
/// cannot wait for a backend to be built.</para></summary>
public interface IVectorProvider : IProviderCall<VectorRequest, VectorResponse>;
