using System.Security.Cryptography;
using Lyntai;
using Lyntai.Secrets;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;
using InMemoryKeyValueStore = Lyntai.Storage.InMemory.InMemoryKeyValueStore;

// Every test skips off Windows through Skip.IfNot, which the CA1416 platform analyzer cannot see as a guard —
// so it is disabled file-wide here rather than sprinkled per line.
#pragma warning disable CA1416

namespace Lyntai.Tests.Secrets;

/// <summary>The Windows-DPAPI protector + the DPAPI-backed envelope vault. Windows-only: off Windows each
/// test SKIPS, visibly, rather than passing as a no-op.</summary>
public class DpapiSecretProtectorTests
{
    [SkippableFact]
    public void Round_trips_and_seals_at_rest()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        var p = new DpapiSecretProtector();

        var sealed1 = p.Protect("super-secret-token");
        Assert.DoesNotContain("super-secret-token", sealed1); // DPAPI ciphertext, base64
        Assert.Equal("super-secret-token", p.Unprotect(sealed1));
    }

    [SkippableFact]
    public void Entropy_is_required_to_unseal()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        var withEntropy = new DpapiSecretProtector(entropy: "app-pepper");
        var sealed1 = withEntropy.Protect("value");

        // the same principal WITHOUT the entropy can't unseal it
        var noEntropy = new DpapiSecretProtector();
        Assert.ThrowsAny<CryptographicException>(() => noEntropy.Unprotect(sealed1));
        Assert.Equal("value", withEntropy.Unprotect(sealed1)); // with the entropy it round-trips
    }

    [SkippableFact]
    public void Corrupt_blob_fails_as_a_cryptographic_exception()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        var p = new DpapiSecretProtector();
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect("!!! not base64 !!!"));
        Assert.ThrowsAny<CryptographicException>(() => p.Unprotect(Convert.ToBase64String(new byte[32]))); // valid base64, not a DPAPI blob
    }

    [SkippableFact]
    public async Task Dpapi_envelope_vault_round_trips_with_a_recovery_key()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
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

    [SkippableFact]
    public async Task AddDpapiSecretVault_wires_the_vault_and_exposes_generate_recover()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "DPAPI is Windows-only");
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
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
