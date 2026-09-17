using Lyntai.Lifecycle;

namespace Lyntai.Tests.Fakes;

/// <summary>Test-only ergonomics over the provider call seams: hand them inputs, get the payload.
///
/// <para><b>It deliberately DROPS the verdict</b>, which is exactly right for the tests that use it — they
/// assert on the vectors and would otherwise repeat the same unwrap on every line. A test that cares how a
/// call FAILED calls <c>CallAsync</c> directly and reads <see cref="VectorResponse.Verdict"/>; that is the
/// behaviour D153 added, so hiding it behind this shim in those tests would test the shim.</para></summary>
internal static class ProviderCallExtensions
{
    public static async Task<IReadOnlyList<float[]>> EmbedAsync(
        this IVectorProvider provider, IReadOnlyList<string> texts, CancellationToken ct = default) =>
        (await provider.CallAsync(new VectorRequest(texts), ct).ConfigureAwait(false)).Vectors;

    public static async Task<IReadOnlyList<float[]>> EmbedAsync(
        this IVectorProvider provider, IReadOnlyList<string> texts, EmbeddingRole role,
        CancellationToken ct = default) =>
        (await provider.CallAsync(new VectorRequest(texts, role), ct).ConfigureAwait(false)).Vectors;

    /// <summary>The same for the HTTP transport, which is not a provider — it has no id and no
    /// capabilities, because the provider that owns it is the backend.</summary>
    public static async Task<IReadOnlyList<float[]>> EmbedAsync(
        this Lyntai.Providers.Http.HttpVectorTransport transport, IReadOnlyList<string> texts,
        CancellationToken ct = default) =>
        (await transport.CallAsync(new VectorRequest(texts), ct).ConfigureAwait(false)).Vectors;

    public static async Task<IReadOnlyList<float[]>> EmbedAsync(
        this Lyntai.Providers.Http.HttpVectorTransport transport, IReadOnlyList<string> texts,
        EmbeddingRole role, CancellationToken ct = default) =>
        (await transport.CallAsync(new VectorRequest(texts, role), ct).ConfigureAwait(false)).Vectors;

    /// <summary>The same, for a variable typed as the base seam. Casts rather than type-tests: a test that
    /// reaches here with a backend that does not embed has a bug worth a hard failure.</summary>
    public static Task<IReadOnlyList<float[]>> EmbedAsync(
        this IModelProvider provider, IReadOnlyList<string> texts, CancellationToken ct = default) =>
        ((IVectorProvider)provider).EmbedAsync(texts, ct);

    public static Task<IReadOnlyList<float[]>> EmbedAsync(
        this IModelProvider provider, IReadOnlyList<string> texts, EmbeddingRole role,
        CancellationToken ct = default) =>
        ((IVectorProvider)provider).EmbedAsync(texts, role, ct);

    public static async Task<IReadOnlyList<double>> ScoreAsync(
        this IScoreProvider provider, string query, IReadOnlyList<string> documents,
        CancellationToken ct = default) =>
        (await provider.CallAsync(new ScoreRequest(query, documents), ct).ConfigureAwait(false)).Scores;

    /// <summary>The same for the HTTP rerank transport, which is not a provider.</summary>
    public static async Task<IReadOnlyList<double>> ScoreAsync(
        this Lyntai.Providers.Http.HttpRerankTransport transport, string query,
        IReadOnlyList<string> documents, CancellationToken ct = default) =>
        (await transport.CallAsync(new ScoreRequest(query, documents), ct).ConfigureAwait(false)).Scores;
}
