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
/// <para>A SYMMETRIC model ignores it, which is why <see cref="IEmbedder"/>'s role-aware overload has a
/// default body.</para></summary>
public enum EmbeddingRole
{
    /// <summary>Text being stored and later searched over. <b>The enum's default</b>, deliberately: a
    /// corpus embedded consistently is still searchable, where one embedded as queries is comparable to
    /// nothing.</summary>
    Document = 0,

    /// <summary>Text being searched WITH, against previously stored <see cref="Document"/> vectors.</summary>
    Query = 1,
}

/// <summary>
/// Turns text into embedding vectors. App-provided (bring your own model — an OpenAI/Ollama embeddings
/// endpoint, a local model, …), registered with <c>builder.AddEmbeddings(...)</c>: Lyntai owns the
/// semantic-recall machinery, the app owns the embedding backend. The batch shape is the primitive
/// (embedding N texts in one call is what real endpoints reward);
/// <see cref="EmbedderExtensions.EmbedAsync(IEmbedder, string, CancellationToken)"/> is the single-text
/// convenience. Every vector an implementation returns MUST have the same dimension —
/// across BOTH roles, since a query vector is compared against document vectors.
/// </summary>
public interface IEmbedder
{
    /// <summary>Embed a batch of texts into vectors — one per input, in the same order. Role-less: the
    /// caller is not saying which side of a comparison these are, so a role-aware implementation should
    /// treat them as <see cref="EmbeddingRole.Document"/>.</summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);

    /// <summary>Embed a batch for a known <paramref name="role"/> — what every Lyntai call site uses, so an
    /// implementation that overrides this sees the role on every embedding the library asks for.
    ///
    /// <para><b>It has a DEFAULT BODY on purpose</b>, forwarding to the role-less overload: an embedder
    /// written before this existed keeps working untouched, and a symmetric model needs nothing else.
    /// Override it only for a model whose two sides genuinely differ.</para></summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken ct = default) =>
        EmbedAsync(texts, ct);
}

public static class EmbedderExtensions
{
    /// <summary>Embed a single text (a thin wrapper over the batch primitive).</summary>
    public static async Task<float[]> EmbedAsync(this IEmbedder embedder, string text, CancellationToken ct = default) =>
        (await embedder.EmbedAsync([text], ct).ConfigureAwait(false))[0];

    /// <summary>Embed a single text for a known <paramref name="role"/>.</summary>
    public static async Task<float[]> EmbedAsync(
        this IEmbedder embedder, string text, EmbeddingRole role, CancellationToken ct = default) =>
        (await embedder.EmbedAsync([text], role, ct).ConfigureAwait(false))[0];
}
