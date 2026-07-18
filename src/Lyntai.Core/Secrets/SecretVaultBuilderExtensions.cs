using Lyntai.Secrets;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Lyntai;

public static class SecretVaultBuilderExtensions
{
    /// <summary>Register the secret vault (design §9). Supply a 32-byte <paramref name="encryptionKey"/>
    /// for AES-256-GCM encryption at rest (BYO key — Lyntai never generates or stores it); omit it for
    /// plaintext (dev only). Pass an <paramref name="accessPolicy"/> to gate reads. Backing: the registered
    /// <see cref="IKeyValueStore"/> (persistent, encrypted) when storage is wired, else in-memory.</summary>
    public static LyntaiBuilder AddSecretVault(this LyntaiBuilder builder,
        byte[]? encryptionKey = null, ISecretAccessPolicy? accessPolicy = null)
    {
        ISecretProtector protector = encryptionKey is { Length: > 0 }
            ? new AesGcmSecretProtector(encryptionKey)
            : new NullSecretProtector();

        builder.Services.TryAddSingleton<ISecretVault>(sp =>
        {
            var kv = sp.GetService<IKeyValueStore>();
            var policy = accessPolicy ?? sp.GetService<ISecretAccessPolicy>();
            return kv is not null
                ? new KeyValueSecretVault(kv, protector, policy)
                : new InMemorySecretVault(protector, policy);
        });
        return builder;
    }

    /// <summary>Register an <see cref="EnvelopeSecretVault"/> — a Lyntai-managed DEK (double-wrapped by a
    /// machine protector + a recovery key) instead of a BYO key. Supply the machine-binding
    /// <paramref name="machineProtector"/> (a DPAPI protector on Windows, or any <see cref="ISecretProtector"/>
    /// elsewhere). The app calls <see cref="EnvelopeSecretVault.GenerateMasterKeyAsync"/> once (recording the
    /// recovery key) and <see cref="EnvelopeSecretVault.RecoverAsync"/> on machine migration. Requires a
    /// registered <see cref="IKeyValueStore"/> (durable jobs-style storage) — the vault is backed by it.</summary>
    public static LyntaiBuilder AddEnvelopeSecretVault(this LyntaiBuilder builder,
        ISecretProtector machineProtector, ISecretAccessPolicy? accessPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(machineProtector);
        builder.Services.TryAddSingleton<ISecretVault>(sp =>
        {
            var kv = sp.GetService<IKeyValueStore>()
                ?? throw new InvalidOperationException(
                    "AddEnvelopeSecretVault requires a key-value store — wire a storage backend (e.g. UseSqliteStorage/UseInMemoryStorage) before it.");
            var policy = accessPolicy ?? sp.GetService<ISecretAccessPolicy>();
            return new EnvelopeSecretVault(kv, machineProtector, policy);
        });
        // also resolvable by its concrete type so the app can reach GenerateMasterKeyAsync/RecoverAsync
        builder.Services.TryAddSingleton(sp => (EnvelopeSecretVault)sp.GetRequiredService<ISecretVault>());
        return builder;
    }
}
