using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Lyntai.Secrets;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Microsoft.Extensions.DependencyInjection;

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

        // Derived from the original rather than a FIXED "AA": the wrap is random per run, so a constant
        // replacement silently tampers with NOTHING on the runs where the value already ended that way —
        // the test then asserts that an untouched envelope throws, and fails intermittently. Flipping the
        // last character guarantees a different string every time.
        var last = envelope.RecoveryWrappedDek[^1];
        var tampered = envelope with
        {
            RecoveryWrappedDek = envelope.RecoveryWrappedDek[..^1] + (last == 'A' ? 'B' : 'A'),
        };

        Assert.NotEqual(envelope.RecoveryWrappedDek, tampered.RecoveryWrappedDek);   // the control
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
    public void Downgraded_recovery_iterations_are_rejected_on_unwrap()
    {
        var (envelope, _, recoveryKey) = SecretKeyEnvelope.Create(Machine());
        // a directly-constructed envelope claiming a weak KDF is rejected at the derive choke point as a
        // CryptographicException (not a stray ArgumentOutOfRangeException for 0)
        foreach (var weak in new[] { 1, 0, SecretKeyEnvelope.MinRecoveryIterations - 1 })
        {
            var tampered = envelope with { RecoveryIterations = weak };
            Assert.ThrowsAny<CryptographicException>(() => tampered.UnwrapWithRecoveryKey(recoveryKey));
        }
    }

    [Fact]
    public void FromJson_rejects_downgraded_iterations_and_future_versions()
    {
        var (envelope, _, _) = SecretKeyEnvelope.Create(Machine());

        var lowIter = JsonNode.Parse(envelope.ToJson())!.AsObject();
        lowIter["recoveryIterations"] = 1;
        Assert.ThrowsAny<CryptographicException>(() => SecretKeyEnvelope.FromJson(lowIter.ToJsonString()));

        var zeroIter = JsonNode.Parse(envelope.ToJson())!.AsObject();
        zeroIter["recoveryIterations"] = 0;
        Assert.ThrowsAny<CryptographicException>(() => SecretKeyEnvelope.FromJson(zeroIter.ToJsonString()));

        // a newer envelope format must be rejected, not silently misparsed as v1
        var future = JsonNode.Parse(envelope.ToJson())!.AsObject();
        future["version"] = SecretKeyEnvelope.CurrentVersion + 1;
        Assert.ThrowsAny<CryptographicException>(() => SecretKeyEnvelope.FromJson(future.ToJsonString()));
    }

    [Fact]
    public void An_envelope_claiming_billions_of_iterations_is_rejected_not_run()
    {
        var (envelope, _, recoveryKey) = SecretKeyEnvelope.Create(Machine());

        // the floor stops a weakened KDF; the same envelope can also name a count that spins for hours
        var huge = JsonNode.Parse(envelope.ToJson())!.AsObject();
        huge["recoveryIterations"] = int.MaxValue;
        Assert.ThrowsAny<CryptographicException>(() => SecretKeyEnvelope.FromJson(huge.ToJsonString()));

        // and a directly-constructed one fails at the derive choke point before any work is done
        var started = DateTime.UtcNow;
        Assert.ThrowsAny<CryptographicException>(
            () => (envelope with { RecoveryIterations = int.MaxValue }).UnwrapWithRecoveryKey(recoveryKey));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Machine_fingerprint_is_stable_and_matches_this_host()
    {
        var (envelope, _, _) = SecretKeyEnvelope.Create(Machine());
        Assert.Equal(SecretKeyEnvelope.ComputeMachineFingerprint(), SecretKeyEnvelope.ComputeMachineFingerprint());
        Assert.True(envelope.MatchesThisMachine);
    }

    // ---- EnvelopeSecretVault ----

    [Fact] // A BYO ISecretVault must not make EnvelopeSecretVault resolution explode with an InvalidCastException
    public void Byo_vault_plus_AddEnvelopeSecretVault_still_resolves_the_concrete_envelope_vault()
    {
        var byo = new InMemorySecretVault(new NullSecretProtector());
        var services = new ServiceCollection();
        services.AddSingleton<ISecretVault>(byo); // BYO interface registration, before AddLyntai
        services.AddLyntai(b => b
            .UseInMemoryStorage()
            .AddEnvelopeSecretVault(Machine()));
        using var sp = services.BuildServiceProvider();

        // the BYO registration wins the interface …
        Assert.Same(byo, sp.GetRequiredService<ISecretVault>());
        // … and the concrete type still resolves coherently, not as a cast of the BYO instance
        Assert.IsType<EnvelopeSecretVault>(sp.GetRequiredService<EnvelopeSecretVault>());
    }

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
    public async Task Two_vaults_over_one_store_generate_one_master_key()
    {
        // two processes over one database: each reads "no envelope" before either writes. A get-then-set lets both
        // write, and the second envelope strands every secret the first process sealed.
        var store = new RacingStore(new InMemoryKeyValueStore());
        var a = new EnvelopeSecretVault(store, Machine());
        var b = new EnvelopeSecretVault(store, Machine());

        var outcomes = await Task.WhenAll(Outcome(a), Outcome(b));

        Assert.Equal(1, outcomes.Count(o => o is null));                        // one vault minted the key
        Assert.IsType<InvalidOperationException>(Assert.Single(outcomes, o => o is not null));
        Assert.Equal(2, store.EnvelopeReads);                                  // both did race past the read
    }

    [Fact]
    public async Task Generating_over_a_store_without_the_capability_keeps_the_old_path()
    {
        // a BYO store that cannot insert-if-absent still generates, and a second generate is still refused
        var store = new PlainStore(new InMemoryKeyValueStore());
        var vault = new EnvelopeSecretVault(store, Machine());

        Assert.NotEmpty(await vault.GenerateMasterKeyAsync());
        await vault.SetAsync("token", "value");
        Assert.Equal("value", await vault.GetAsync("token"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => vault.GenerateMasterKeyAsync());
    }

    private static async Task<Exception?> Outcome(EnvelopeSecretVault vault)
    {
        try { await vault.GenerateMasterKeyAsync(); return null; }
        catch (Exception ex) { return ex; }
    }

    /// <summary>A store whose read of the envelope waits for a second reader, so two vaults both see no envelope
    /// before either writes — the interleaving two processes can produce. Bounded, so a path that reads once
    /// cannot hang the test.</summary>
    private sealed class RacingStore(InMemoryKeyValueStore inner) : IConditionalKeyValueStore
    {
        private readonly TaskCompletionSource _both = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reads;

        public int EnvelopeReads => _reads;

        public async Task<string?> GetAsync(string key, CancellationToken ct = default)
        {
            if (key == EnvelopeSecretVault.EnvelopeKey)
            {
                var n = Interlocked.Increment(ref _reads);
                if (n == 2) _both.TrySetResult();
                if (n <= 2) await _both.Task.WaitAsync(TimeSpan.FromSeconds(5), ct);
            }
            return await inner.GetAsync(key, ct);
        }

        public Task SetAsync(string key, string value, CancellationToken ct = default) => inner.SetAsync(key, value, ct);
        public Task<bool> SetIfAbsentAsync(string key, string value, CancellationToken ct = default) =>
            inner.SetIfAbsentAsync(key, value, ct);
        public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(key, ct);
        public Task<IReadOnlyList<string>> ListKeysAsync(string? prefix = null, CancellationToken ct = default) =>
            inner.ListKeysAsync(prefix, ct);
    }

    /// <summary>A store without the insert-if-absent capability, as a BYO one may be.</summary>
    private sealed class PlainStore(IKeyValueStore inner) : IKeyValueStore
    {
        public Task<string?> GetAsync(string key, CancellationToken ct = default) => inner.GetAsync(key, ct);
        public Task SetAsync(string key, string value, CancellationToken ct = default) => inner.SetAsync(key, value, ct);
        public Task DeleteAsync(string key, CancellationToken ct = default) => inner.DeleteAsync(key, ct);
        public Task<IReadOnlyList<string>> ListKeysAsync(string? prefix = null, CancellationToken ct = default) =>
            inner.ListKeysAsync(prefix, ct);
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
