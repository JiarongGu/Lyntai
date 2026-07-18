using Lyntai.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai;

public static class DpapiSecretVaultBuilderExtensions
{
    /// <summary>Register an <see cref="EnvelopeSecretVault"/> whose DEK is machine-bound with Windows DPAPI
    /// (via <see cref="DpapiSecretProtector"/>) and recoverable off-machine with the recovery key returned
    /// by <see cref="EnvelopeSecretVault.GenerateMasterKeyAsync"/>. This is the "no key to manage, machine-
    /// sealed at rest, but not locked out if the host dies" mode for Windows deployments.
    /// <para>Requires a registered <see cref="Lyntai.Storage.IKeyValueStore"/> (a storage backend). The app
    /// drives the lifecycle: <c>GenerateMasterKeyAsync</c> once on install (record the recovery key),
    /// <c>RecoverAsync</c> on machine migration. <b>Windows-only</b> — the DPAPI protector throws off
    /// Windows; use <c>AddEnvelopeSecretVault</c> with an AES-GCM protector elsewhere.</para></summary>
    /// <param name="builder">The Lyntai builder being configured.</param>
    /// <param name="scope">DPAPI binding scope (default: current user).</param>
    /// <param name="entropy">Optional extra secret mixed into the DPAPI binding.</param>
    /// <param name="accessPolicy">Optional read gate for the vault.</param>
    public static LyntaiBuilder AddDpapiSecretVault(this LyntaiBuilder builder,
        DpapiScope scope = DpapiScope.CurrentUser, string? entropy = null, ISecretAccessPolicy? accessPolicy = null) =>
        builder.AddEnvelopeSecretVault(new DpapiSecretProtector(scope, entropy), accessPolicy);
}
