using Lyntai.Lifecycle;

namespace Lyntai.Tests.Fakes;

/// <summary>Base for a test embedding backend: supplies the <see cref="IModelProvider"/> bookkeeping —
/// an id and a <see cref="ProviderKinds.Vector"/> declaration — so a fake only writes the part that
/// differs, which is how the vectors are produced.
///
/// <para><b>Why the fakes are providers at all (D151).</b> There is no vector backend seam left to implement.
/// A fake that declares the capability goes through the SAME filter and routing the production path uses,
/// so a test can no longer pass by implementing an interface nothing but tests implements.</para>
///
/// <para>The role-aware overload forwards to the role-less one, exactly as
/// <see cref="IModelProvider"/>'s own default does; a fake that cares about the role overrides it.</para>
/// </summary>
public abstract class FakeVectorProviderBase : IModelProvider
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

    public abstract Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, CancellationToken ct = default);

    public virtual Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts, EmbeddingRole role, CancellationToken ct = default) =>
        EmbedAsync(texts, ct);
}
