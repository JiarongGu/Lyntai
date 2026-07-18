using System.Security.Cryptography;
using Lyntai.Secrets;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Secrets;

/// <summary>The DEK-envelope crypto + the envelope-backed vault. The "machine protector" is stubbed with
/// an <see cref="AesGcmSecretProtector"/> so these run cross-platform; a "different machine" is simulated
/// by an AES-GCM protector holding a different key (its unwrap fails exactly like a foreign DPAPI blob).</summary>
public class SecretKeyEnvelopeTests
{
    private static AesGcmSecretProtector Machine() => new(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Create_then_unwrap_with_machine_round_trips()
    {
        var machine = Machine();
        var (envelope, dek, recoveryKey) = SecretKeyEnvelope.Create(machine);

        Assert.Equal(dek, envelope.UnwrapWithMachine(machine));   // fast path recovers the same DEK
        Assert.NotEmpty(recoveryKey);
        Assert.DoesNotContain(Convert.ToBase64String(dek), envelope.MachineWrappedDek); // DEK not in the clear
    }

    [Fact]
    public void Recovery_key_unwraps_on_a_different_machine()
    {
        var (envelope, dek, recoveryKey) = SecretKeyEnvelope.Create(Machine());

        // a DIFFERENT machine (another protector key) can't unseal the machine wrap...
        Assert.ThrowsAny<CryptographicException>(() => envelope.UnwrapWithMachine(Machine()));
        // ...but the recovery key still yields the DEK
        Assert.Equal(dek, envelope.UnwrapWithRecoveryKey(recoveryKey));
    }

    [Fact]
    public void Wrong_recovery_key_throws()
    {
        var (envelope, _, _) = SecretKeyEnvelope.Create(Machine());
        Assert.ThrowsAny<CryptographicException>(() => envelope.UnwrapWithRecoveryKey("not-the-key"));
        Assert.ThrowsAny<CryptographicException>(() => envelope.UnwrapWithRecoveryKey(""));
    }

    [Fact]
    public void Tampered_recovery_wrap_throws()
    {
        var (envelope, _, recoveryKey) = SecretKeyEnvelope.Create(Machine());
        var tampered = envelope with { RecoveryWrappedDek = envelope.RecoveryWrappedDek[..^2] + "AA" };
        Assert.ThrowsAny<CryptographicException>(() => tampered.UnwrapWithRecoveryKey(recoveryKey));
    }

    [Fact]
    public void Json_round_trips_and_preserves_both_wraps()
    {
        var machine = Machine();
        var (envelope, dek, recoveryKey) = SecretKeyEnvelope.Create(machine);

        var back = SecretKeyEnvelope.FromJson(envelope.ToJson());
        Assert.Equal(dek, back.UnwrapWithMachine(machine));
        Assert.Equal(dek, back.UnwrapWithRecoveryKey(recoveryKey));
        Assert.Equal(envelope.MachineFingerprint, back.MachineFingerprint);
    }

    [Fact]
    public void Rewrap_for_machine_rebinds_the_dek_and_keeps_recovery()
    {
        var (envelope, dek, recoveryKey) = SecretKeyEnvelope.Create(Machine());
        var newMachine = Machine();

        var rebound = envelope.RewrapForMachine(dek, newMachine);
        Assert.Equal(dek, rebound.UnwrapWithMachine(newMachine));       // now unwraps on the new host
        Assert.Equal(dek, rebound.UnwrapWithRecoveryKey(recoveryKey));  // recovery wrap preserved
    }

    [Fact]
    public void Machine_fingerprint_is_stable_and_matches_this_host()
    {
        var (envelope, _, _) = SecretKeyEnvelope.Create(Machine());
        Assert.Equal(SecretKeyEnvelope.ComputeMachineFingerprint(), SecretKeyEnvelope.ComputeMachineFingerprint());
        Assert.True(envelope.MatchesThisMachine);
    }

    // ---- EnvelopeSecretVault ----

    [Fact]
    public async Task Generate_then_secrets_round_trip_and_are_ciphertext_at_rest()
    {
        var kv = new InMemoryKeyValueStore();
        var vault = new EnvelopeSecretVault(kv, Machine());

        var recoveryKey = await vault.GenerateMasterKeyAsync();
        Assert.NotEmpty(recoveryKey);

        await vault.SetAsync("api-key", "sk-live-plaintext");
        Assert.Equal("sk-live-plaintext", await vault.GetAsync("api-key"));

        var atRest = await kv.GetAsync("lyntai:secret:api-key");
        Assert.NotNull(atRest);
        Assert.DoesNotContain("sk-live-plaintext", atRest);
    }

    [Fact]
    public async Task Generate_twice_throws_to_avoid_stranding_secrets()
    {
        var vault = new EnvelopeSecretVault(new InMemoryKeyValueStore(), Machine());
        await vault.GenerateMasterKeyAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.GenerateMasterKeyAsync());
    }

    [Fact]
    public async Task Get_before_generate_throws()
    {
        var vault = new EnvelopeSecretVault(new InMemoryKeyValueStore(), Machine());
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.GetAsync("x"));
    }

    [Fact]
    public async Task A_new_machine_needs_recovery_then_reads_the_original_secret()
    {
        // machine A creates the vault + a secret in a shared KV
        var kv = new InMemoryKeyValueStore();
        var machineA = Machine();
        var vaultA = new EnvelopeSecretVault(kv, machineA);
        var recoveryKey = await vaultA.GenerateMasterKeyAsync();
        await vaultA.SetAsync("token", "value-from-machine-A");

        // machine B opens the SAME store with a different machine protector → can't unseal the DEK
        var machineB = Machine();
        var vaultB = new EnvelopeSecretVault(kv, machineB);
        await Assert.ThrowsAsync<SecretRecoveryRequiredException>(() => vaultB.GetAsync("token"));

        // recover with the key → re-binds to machine B and reads the original secret
        await vaultB.RecoverAsync(recoveryKey);
        Assert.Equal("value-from-machine-A", await vaultB.GetAsync("token"));

        // and now the fast path works on machine B without the recovery key
        var vaultB2 = new EnvelopeSecretVault(kv, machineB);
        Assert.Equal("value-from-machine-A", await vaultB2.GetAsync("token"));
    }

    [Fact]
    public async Task Recover_with_wrong_key_throws_and_leaves_the_vault_sealed()
    {
        var kv = new InMemoryKeyValueStore();
        var vaultA = new EnvelopeSecretVault(kv, Machine());
        await vaultA.GenerateMasterKeyAsync();
        await vaultA.SetAsync("token", "secret");

        var vaultB = new EnvelopeSecretVault(kv, Machine());
        await Assert.ThrowsAnyAsync<CryptographicException>(() => vaultB.RecoverAsync("wrong-recovery-key"));
        await Assert.ThrowsAsync<SecretRecoveryRequiredException>(() => vaultB.GetAsync("token"));
    }
}
