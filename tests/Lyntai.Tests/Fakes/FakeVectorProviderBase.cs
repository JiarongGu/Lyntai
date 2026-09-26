using Lyntai.Inference;

namespace Lyntai.Tests.Fakes;

/// <summary>Base for a test embedding backend: supplies the <see cref="IModelProvider"/> bookkeeping —
/// an id and a <see cref="ProviderKinds.Vector"/> declaration — so a fake only writes the part that
/// differs, which is how the vectors are produced.
///
/// <para><b>Why the fakes are providers at all (D151).</b> There is no separate vector backend seam.
/// A fake that declares the capability goes through the SAME filter and routing the production path uses,
/// so a test cannot pass by implementing an interface nothing but tests implements.</para>
///
/// <para>The role-aware overload forwards to the role-less one, exactly as
/// <see cref="IModelProvider"/>'s own default does; a fake that cares about the role overrides it.</para>
/// </summary>
public abstract class FakeVectorProviderBase : IVectorProvider
{
    public string Id { get; init; } = "fake-vectors";

    /// <summary>The declaration, reachable without an instance — see <see cref="FakeVectorProvider.Declared"/>.</summary>
    public static readonly ProviderCapabilities Declared = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [ProviderKinds.Vector],
        Operations = [ProviderOperation.Complete],
    };

    public ProviderCapabilities Capabilities => Declared;

    /// <summary>The seam a fake actually implements. <b>Adapting it to
    /// <see cref="IProviderCall{TRequest,TResponse}"/> here rather than in each fake is deliberate</b>: the
    /// twenty-odd fakes below differ only in how they produce vectors, and a fake that THROWS still throws
    /// out of <see cref="CallAsync"/> — which is what its test means, since the router classifies an escaping
    /// exception exactly as it would from a real backend.</summary>
    public abstract Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);

    /// <summary>The role-aware form, forwarding to the role-less one; a fake that cares overrides it.</summary>
    public virtual Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken ct = default) =>
        EmbedAsync(texts, ct);

    /// <inheritdoc />
    public async Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default)
    {
        var vectors = await EmbedAsync(request.Texts, request.Role, ct).ConfigureAwait(false);
        // an empty ASK is a truthful empty Ok; Success guards the other case, an Ok that answered a real
        // request with nothing
        return vectors.Count == 0
            ? new VectorResponse(ProviderVerdict.Ok, vectors)
            : VectorResponse.Success(vectors);
    }
}
