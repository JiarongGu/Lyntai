namespace Lyntai.Embeddings;

/// <summary>Which side of a similarity comparison a text is being embedded for.
///
/// <para><b>Why the library says this at all.</b> Asymmetric embedding models are trained with a distinct
/// instruction per side — the E5, BGE, nomic and Arctic families all ship one prefix for stored text and
/// another for the search text — and score materially worse when both sides are embedded identically.
/// Which model, and which prefix, is the deployment's choice; Lyntai supplies only the fact that a given
/// call is one side or the other, because that is the one thing an implementation cannot work out for
/// itself.</para>
///
/// <para>A SYMMETRIC model ignores it, which is why
/// <see cref="Lyntai.Lifecycle.IModelProvider.EmbedAsync(IReadOnlyList{string}, EmbeddingRole, CancellationToken)"/>
/// has a default body forwarding to the role-less overload.</para></summary>
public enum EmbeddingRole
{
    /// <summary>Text being stored and later searched over. <b>The enum's default</b>, deliberately: a
    /// corpus embedded consistently is still searchable, where one embedded as queries is comparable to
    /// nothing.</summary>
    Document = 0,

    /// <summary>Text being searched WITH, against previously stored <see cref="Document"/> vectors.</summary>
    Query = 1,
}
