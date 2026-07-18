using System.Security.Cryptography;
using Lyntai;
using Lyntai.Secrets;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using InMemoryKeyValueStore = Lyntai.Storage.InMemory.InMemoryKeyValueStore;

// Every test guards at runtime with OperatingSystem.IsWindows() and returns early off Windows; the CA1416
// analyzer recognizes that guard for direct calls but not for the DPAPI calls nested inside ThrowsAny(...)
// lambdas — disable it file-wide here rather than sprinkle per-line suppressions.
#pragma warning disable CA1416

namespace Lyntai.Tests.Secrets;

/// <summary>The Windows-DPAPI protector + the DPAPI-backed envelope vault. Windows-only: off Windows each
/// test is a no-op pass (xUnit v2 has no dynamic Assert.Skip) — this machine is Windows, so it runs here.
/// The <c>OperatingSystem.IsWindows()</c> guard is inline (not a wrapper property) so the CA1416 platform
/// analyzer recognizes it and doesn't flag the DPAPI calls that follow.</summary>
public class DpapiSecretProtectorTests
{
    [Fact]
    public void Round_trips_and_seals_at_rest()
    {
        if (!OperatingSystem.IsWindows()) return;
        var p = new DpapiSecretProtector();

        var sealed1 = p.Protect("super-secret-token");
        Assert.DoesNotContain("super-secret-token", sealed1); // DPAPI ciphertext, base64
        Assert.Equal("super-secret-token", p.Unprotect(sealed1));
    }

    [Fact]
    public void Entropy_is_required_to_unseal()
    {
        if (!OperatingSystem.IsWindows()) return;
        var withEntropy = new DpapiSecretProtector(entropy: "app-pepper");
        var sealed1 = withEntropy.Protect("value");

        // the same principal WITHOUT the entropy can't unseal it
        var noEntropy = new DpapiSecretProtector();
        Assert.ThrowsAny<CryptographicException>(() => noEntropy.Unprotect(sealed1));
        Assert.Equal("value", withEntropy.Unprotect(sealed1)); // with the entropy it round-trips
    }

    [Fact]
    public void Corrupt_blob_fails_as_a_cryptographic_exception()
    {
        if (!OperatingSystem.IsWindows()) return;
        var p = new DpapiSecretProtector();
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect("!!! not base64 !!!"));
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(Convert.ToBase64String(new byte[32]))); // valid base64, not a DPAPI blob
    }

    [Fact]
    public async Task Dpapi_envelope_vault_round_trips_with_a_recovery_key()
    {
        if (!OperatingSystem.IsWindows()) return;
        var kv = new InMemoryKeyValueStore();
        var vault = new EnvelopeSecretVault(kv, new DpapiSecretProtector());

        var recoveryKey = await vault.GenerateMasterKeyAsync();
        await vault.SetAsync("api-key", "sk-dpapi-sealed");

        Assert.Equal("sk-dpapi-sealed", await vault.GetAsync("api-key"));
        Assert.NotEmpty(recoveryKey);

        // a fresh vault instance over the same store re-opens via the machine (DPAPI) fast path
        var reopened = new EnvelopeSecretVault(kv, new DpapiSecretProtector());
        Assert.Equal("sk-dpapi-sealed", await reopened.GetAsync("api-key"));
    }

    [Fact]
    public async Task AddDpapiSecretVault_wires_the_vault_and_exposes_generate_recover()
    {
        if (!OperatingSystem.IsWindows()) return;
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddDpapiSecretVault());
        using var sp = services.BuildServiceProvider();

        var envelope = sp.GetRequiredService<EnvelopeSecretVault>();
        await envelope.GenerateMasterKeyAsync();

        var vault = sp.GetRequiredService<ISecretVault>();
        await vault.SetAsync("token", "abc123");
        Assert.Equal("abc123", await vault.GetAsync("token"));
    }
}
