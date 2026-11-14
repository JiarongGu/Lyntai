using System.Security.Cryptography;
using Lyntai;
using Lyntai.Secrets;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Lyntai.Storage.InMemory;

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

    // ---- a secret is bound to its name ----------------------------------------------------------------------

    private const string At = "lyntai:secret:";
    private const string Marker = "lyntai2:";

    private static AesGcmSecretProtector Aes() => new(RandomNumberGenerator.GetBytes(32));

    /// <summary>The policy gates by NAME: anyone may read "public", nobody may read anything else.</summary>
    private sealed class PublicOnly : ISecretAccessPolicy
    {
        public Task<bool> CanReadAsync(string name, string? accessor, CancellationToken ct = default) =>
            Task.FromResult(name == "public");
    }

    [Fact]
    public async Task A_blob_copied_to_another_name_does_not_decrypt()
    {
        // whoever can write the store but may read only "public" copies the admin blob over it: unbound, it
        // decrypted there and handed the admin token to a reader the policy had refused
        var kv = new InMemoryKeyValueStore();
        var vault = new KeyValueSecretVault(kv, Aes(), new PublicOnly());
        await vault.SetAsync("admin-token", "sk-admin");

        await kv.SetAsync(At + "public", (await kv.GetAsync(At + "admin-token"))!);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => vault.GetAsync("public", "anyone"));
    }

    [Fact]
    public async Task A_blob_copied_to_another_name_does_not_decrypt_through_the_envelope_vault()
    {
        var kv = new InMemoryKeyValueStore();
        var vault = new EnvelopeSecretVault(kv, Aes(), new PublicOnly());
        await vault.GenerateMasterKeyAsync();
        await vault.SetAsync("admin-token", "sk-admin");

        await kv.SetAsync(At + "public", (await kv.GetAsync(At + "admin-token"))!);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => vault.GetAsync("public", "anyone"));
    }

    [Fact]
    public async Task A_legacy_blob_still_reads()
    {
        var kv = new InMemoryKeyValueStore();
        var protector = Aes();
        await kv.SetAsync(At + "old", protector.Protect("legacy-value"));   // as a build before binding wrote it

        Assert.Equal("legacy-value", await new KeyValueSecretVault(kv, protector).GetAsync("old"));
    }

    [Fact]
    public async Task Rebind_rewrites_legacy_blobs()
    {
        var kv = new InMemoryKeyValueStore();
        var protector = Aes();
        await kv.SetAsync(At + "a", protector.Protect("value-a"));
        await kv.SetAsync(At + "b", protector.Protect("value-b"));
        var vault = new KeyValueSecretVault(kv, protector);
        await vault.SetAsync("c", "value-c");                               // already bound

        Assert.Equal(2, await vault.RebindAsync());
        Assert.Equal(0, await vault.RebindAsync());                         // nothing left to rewrite

        foreach (var name in new[] { "a", "b", "c" })
            Assert.StartsWith(Marker, await kv.GetAsync(At + name));
        Assert.Equal("value-a", await vault.GetAsync("a"));
        Assert.Equal("value-b", await vault.GetAsync("b"));

        // the rewritten blob is bound: copied over another name, it no longer decrypts there
        await kv.SetAsync(At + "b", (await kv.GetAsync(At + "a"))!);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => vault.GetAsync("b"));
    }

    [Fact]
    public async Task RefuseUnboundSecrets_refuses_a_legacy_blob()
    {
        var kv = new InMemoryKeyValueStore();
        var protector = Aes();
        await kv.SetAsync(At + "old", protector.Protect("legacy-value"));
        var vault = new KeyValueSecretVault(kv, protector) { RefuseUnboundSecrets = true };

        await Assert.ThrowsAnyAsync<CryptographicException>(() => vault.GetAsync("old"));

        await vault.SetAsync("new", "bound-value");
        Assert.Equal("bound-value", await vault.GetAsync("new"));

        // the upgrade path: the rebind still reads what the vault refuses, and binds it
        Assert.Equal(1, await vault.RebindAsync());
        Assert.Equal("legacy-value", await vault.GetAsync("old"));
    }

    [Fact]
    public async Task A_marked_blob_stripped_of_its_marker_fails()
    {
        // without its marker the blob takes the legacy path, which would hand back the binding header as the value
        var kv = new InMemoryKeyValueStore();
        var vault = new KeyValueSecretVault(kv, Aes());
        await vault.SetAsync("a", "value");
        var stored = (await kv.GetAsync(At + "a"))!;
        Assert.StartsWith(Marker, stored);

        await kv.SetAsync(At + "a", stored[Marker.Length..]);
        await kv.SetAsync(At + "b", stored[Marker.Length..]);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => vault.GetAsync("a"));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => vault.GetAsync("b"));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => vault.RebindAsync());
    }

    [Fact]
    public async Task The_envelope_vault_passes_the_refusal_and_the_rebind_through()
    {
        var kv = new InMemoryKeyValueStore();
        var recoveryKey = await new EnvelopeSecretVault(kv, Aes()).GenerateMasterKeyAsync();
        var dek = SecretKeyEnvelope.FromJson((await kv.GetAsync(EnvelopeSecretVault.EnvelopeKey))!)
            .UnwrapWithRecoveryKey(recoveryKey);
        await kv.SetAsync(At + "old", new AesGcmSecretProtector(dek).Protect("legacy-value"));

        var vault = new EnvelopeSecretVault(kv, Aes()) { RefuseUnboundSecrets = true };
        await vault.RecoverAsync(recoveryKey);

        await Assert.ThrowsAnyAsync<CryptographicException>(() => vault.GetAsync("old"));
        Assert.Equal(1, await vault.RebindAsync());
        Assert.Equal("legacy-value", await vault.GetAsync("old"));
    }

    [Fact]
    public async Task The_builder_carries_the_refusal_to_either_vault()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddSecretVault(RandomNumberGenerator.GetBytes(32), accessPolicy: null, refuseUnboundSecrets: true));
        using (var sp = services.BuildServiceProvider())
            Assert.True(Assert.IsType<KeyValueSecretVault>(sp.GetRequiredService<ISecretVault>()).RefuseUnboundSecrets);

        var envelope = new ServiceCollection();
        envelope.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddEnvelopeSecretVault(Aes(), accessPolicy: null, refuseUnboundSecrets: true));
        using (var sp = envelope.BuildServiceProvider())
            Assert.True(sp.GetRequiredService<EnvelopeSecretVault>().RefuseUnboundSecrets);
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
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddSecretVault(RandomNumberGenerator.GetBytes(32)));
        using var sp = services.BuildServiceProvider();

        var vault = sp.GetRequiredService<ISecretVault>();
        await vault.SetAsync("token", "abc123");
        Assert.Equal("abc123", await vault.GetAsync("token"));
    }

    [Fact]
    public void AddSecretVault_refuses_a_missing_or_empty_key_and_points_at_the_plaintext_opt_in()
    {
        var services = new ServiceCollection();
        // a "secret vault" must never silently store plaintext — no key is a hard error at wiring time,
        // and the message must name the loud dev-only escape hatch
        foreach (var badKey in new[] { null, Array.Empty<byte>() })
        {
            var ex = Assert.Throws<ArgumentException>(() =>
                services.AddLyntai(b => b.AddProvider(_ => new FakeTextProvider("p")).AddSecretVault(badKey!)));
            Assert.Contains("AddPlaintextSecretVault", ex.Message);
        }
    }

    [Fact]
    public async Task AddPlaintextSecretVault_round_trips_and_stores_plaintext_at_rest()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddPlaintextSecretVault());
        using var sp = services.BuildServiceProvider();

        var vault = sp.GetRequiredService<ISecretVault>();
        await vault.SetAsync("token", "dev-only-value");
        Assert.Equal("dev-only-value", await vault.GetAsync("token"));

        // the deliberate trade-off of the loud opt-in: the backing KV holds the PLAINTEXT
        var kv = sp.GetRequiredService<Lyntai.Storage.IKeyValueStore>();
        Assert.Equal("dev-only-value", await kv.GetAsync("lyntai:secret:token"));
    }
}
