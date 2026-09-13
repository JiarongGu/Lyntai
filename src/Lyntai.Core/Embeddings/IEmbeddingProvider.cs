namespace Lyntai.Embeddings;

/// <summary>An embedding backend that carries provider IDENTITY — the same shape
/// <see cref="Lyntai.Llm.ILlmProvider"/> and <see cref="Lyntai.Generation.IGenerationProvider"/> have, for
/// the same reasons.
///
/// <para><b>Why this is additive rather than a change to <see cref="IEmbedder"/>.</b> Those two seams could
/// adopt <see cref="Lyntai.Lifecycle.IProviderIdentity"/> as a base because they already declared
/// <c>Id</c>; <see cref="IEmbedder"/> does not, so adding the base would introduce a REQUIRED member and
/// break every bring-your-own embedder at compile. An embedder is still an embedder — this is the optional
/// capability pattern Core already uses for <see cref="Lyntai.Generation.IGenerationJobProvider"/>: a
/// backend that has an identity declares one, and a lambda-shaped embedder stays legal.</para>
///
/// <para><b>What it buys.</b> Lifetime machinery written once against
/// <see cref="Lyntai.Lifecycle.IProviderIdentity"/> reaches embedders too, a deployment can register more
/// than one and tell them apart, and a diagnostic can finally say WHICH embedder produced a vector — the
/// question the single-registration seam cannot answer.</para></summary>
public interface IEmbeddingProvider : Lyntai.Lifecycle.IProviderIdentity, IEmbedder
{
    /// <summary>Stable id a deployment names this backend by (<c>"static"</c>, <c>"onnx"</c>,
    /// <c>"openai"</c>).</summary>
    /// <remarks><b>Declared here as well as on <see cref="Lyntai.Lifecycle.IProviderIdentity"/> on purpose,
    /// and <c>new</c> only to silence CS0108</b> — the same reason the other two seams do it. Member
    /// resolution does not walk base INTERFACES, so a caller compiled against this declaration keeps
    /// binding if the base is ever restructured. Pinned by
    /// <c>ProviderIdentityTests.Every_seam_still_declares_Id_itself</c>.</remarks>
    new string Id { get; }

    /// <summary>Cheap probe: is this backend usable right now — model loaded, endpoint configured? Must
    /// return a value rather than throw, matching <see cref="Lyntai.Llm.ILlmProvider.IsAvailable"/>.</summary>
    bool IsAvailable { get; }
}
