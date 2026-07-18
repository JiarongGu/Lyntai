using System.Security.Cryptography;
using Lyntai;
using Lyntai.Secrets;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Secrets;

public class SecretVaultTests
{
    [Fact]
    public void AesGcm_round_trips_and_rejects_tampering()
    {
        var p = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));

        var sealed1 = p.Protect("super-secret-token");
        Assert.DoesNotContain("super-secret-token", sealed1);   // ciphertext, not plaintext
        Assert.Equal("super-secret-token", p.Unprotect(sealed1));

        var tampered = sealed1[..^2] + (sealed1[^1] == 'A' ? "BB" : "AA");
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(tampered)); // auth tag catches it
    }

    [Fact]
    public void AesGcm_requires_a_32_byte_key()
    {
        Assert.Throws<ArgumentException>(() => new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(16)));
    }

    [Fact]
    public void Unprotect_fails_a_corrupt_blob_as_a_cryptographic_exception_not_a_stray_type()
    {
        var p = new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32));
        // every unusable at-rest blob must surface as CryptographicException (one type a caller can catch),
        // not a FormatException / ArgumentOutOfRangeException leaking from base64 parsing or span slicing
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect("!!! not base64 !!!"));            // non-base64
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(Convert.ToBase64String(new byte[10]))); // < nonce+tag
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(Convert.ToBase64String(new byte[40]))); // right size, bad tag
        Assert.Equal("ok", p.Unprotect(p.Protect("ok"))); // and a real blob still round-trips
    }

    [Fact]
    public async Task Kv_vault_stores_ciphertext_at_rest_and_lists_names()
    {
        var kv = new InMemoryKeyValueStore();
        var vault = new KeyValueSecretVault(kv, new AesGcmSecretProtector(RandomNumberGenerator.GetBytes(32)));

        await vault.SetAsync("api-key", "sk-plaintext-value");
        await vault.SetAsync("db-pass", "hunter2");

        // at rest, the KV holds ciphertext — the plaintext is nowhere in the raw store
        var atRest = await kv.GetAsync("lyntai:secret:api-key");
        Assert.NotNull(atRest);
        Assert.DoesNotContain("sk-plaintext-value", atRest);

        Assert.Equal("sk-plaintext-value", await vault.GetAsync("api-key"));   // decrypts on read
        Assert.Null(await vault.GetAsync("missing"));
        Assert.Equal(["api-key", "db-pass"], (await vault.ListNamesAsync()).OrderBy(n => n));

        await vault.DeleteAsync("api-key");
        Assert.Null(await vault.GetAsync("api-key"));
        Assert.Equal(["db-pass"], await vault.ListNamesAsync());
    }

    private sealed class OwnerOnly : ISecretAccessPolicy
    {
        public Task<bool> CanReadAsync(string name, string? accessor, CancellationToken ct = default) =>
            Task.FromResult(accessor == "owner");
    }

    [Fact]
    public async Task Access_gate_denies_unauthorized_reads()
    {
        var vault = new InMemorySecretVault(policy: new OwnerOnly());
        await vault.SetAsync("k", "v");

        Assert.Equal("v", await vault.GetAsync("k", accessor: "owner"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => vault.GetAsync("k", accessor: "stranger"));
    }

    [Fact]
    public async Task AddSecretVault_wires_an_encrypted_kv_backed_vault()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddSecretVault(RandomNumberGenerator.GetBytes(32)));
        using var sp = services.BuildServiceProvider();

        var vault = sp.GetRequiredService<ISecretVault>();
        await vault.SetAsync("token", "abc123");
        Assert.Equal("abc123", await vault.GetAsync("token"));
    }
}
