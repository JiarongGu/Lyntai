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
}
