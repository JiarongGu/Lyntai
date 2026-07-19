using System.Security.Cryptography;
using Lyntai.Storage;

namespace Lyntai.Secrets;

/// <summary>
/// An <see cref="ISecretVault"/> whose at-rest key is a Lyntai-managed <see cref="SecretKeyEnvelope"/>
/// rather than a BYO key: all secrets are AES-256-GCM encrypted under a random DEK, and the DEK is
/// machine-wrapped (fast path) + recovery-key-wrapped (portability path) in the envelope. This is the
/// "no key to manage, but not locked out if the machine dies" mode.
/// <para>Lifecycle (the app drives it): call <see cref="GenerateMasterKeyAsync"/> ONCE on a fresh install
/// and record the returned recovery key out-of-band; thereafter reads/writes auto-initialize via the
/// machine wrap. On a new machine (machine unwrap fails) the vault throws
/// <see cref="SecretRecoveryRequiredException"/> until <see cref="RecoverAsync"/> is called with the
/// recovery key, which re-binds the DEK to the new host.</para>
/// The envelope + secrets both live in the injected <see cref="IKeyValueStore"/>; the DEK never touches
/// the store unwrapped, and the recovery key is never stored at all.
/// </summary>
public sealed class EnvelopeSecretVault(
    IKeyValueStore kv, ISecretProtector machineProtector, ISecretAccessPolicy? policy = null) : ISecretVault
{
    internal const string EnvelopeKey = "lyntai:secret-envelope";
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private KeyValueSecretVault? _inner; // built once the DEK is unwrapped

    /// <summary>Mint a new master key (DEK) if the vault has none, persist its envelope, and return the
    /// one-time recovery key for the operator to store out-of-band. Throws
    /// <see cref="InvalidOperationException"/> if an envelope already exists — regenerating would strand
    /// every secret already encrypted under the old DEK.</summary>
    public async Task<string> GenerateMasterKeyAsync(CancellationToken ct = default)
    {
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (await kv.GetAsync(EnvelopeKey, ct).ConfigureAwait(false) is not null)
                throw new InvalidOperationException(
                    "A secret-key envelope already exists — regenerating would strand secrets encrypted under the old key. " +
                    "Delete the vault (or use RecoverAsync) instead of regenerating.");

            var (envelope, dek, recoveryKey) = SecretKeyEnvelope.Create(machineProtector);
            await kv.SetAsync(EnvelopeKey, envelope.ToJson(), ct).ConfigureAwait(false);
            _inner = BuildInner(dek);
            return recoveryKey;
        }
        finally { _initLock.Release(); }
    }

    /// <summary>Load the envelope and unwrap the DEK via the machine wrap (fast path). Throws
    /// <see cref="InvalidOperationException"/> if no envelope exists (call <see cref="GenerateMasterKeyAsync"/>
    /// first) and <see cref="SecretRecoveryRequiredException"/> if the machine can't unseal it.</summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try { await EnsureInitializedLockedAsync(ct).ConfigureAwait(false); }
        finally { _initLock.Release(); }
    }

    /// <summary>Recover on a machine that can't unseal the machine wrap: unwrap the DEK via the recovery
    /// key, re-bind it to THIS host (a fresh machine wrap), persist the updated envelope, and initialize.
    /// Throws <see cref="CryptographicException"/> if the recovery key is wrong.</summary>
    public async Task RecoverAsync(string recoveryKey, CancellationToken ct = default)
    {
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var envelope = await LoadEnvelopeAsync(ct).ConfigureAwait(false);
            var dek = envelope.UnwrapWithRecoveryKey(recoveryKey); // throws CryptographicException on a bad key
            var rebound = envelope.RewrapForMachine(dek, machineProtector);
            await kv.SetAsync(EnvelopeKey, rebound.ToJson(), ct).ConfigureAwait(false);
            _inner = BuildInner(dek);
        }
        finally { _initLock.Release(); }
    }

    public async Task<string?> GetAsync(string name, string? accessor = null, CancellationToken ct = default) =>
        await (await InnerAsync(ct).ConfigureAwait(false)).GetAsync(name, accessor, ct).ConfigureAwait(false);

    public async Task SetAsync(string name, string value, CancellationToken ct = default) =>
        await (await InnerAsync(ct).ConfigureAwait(false)).SetAsync(name, value, ct).ConfigureAwait(false);

    public async Task DeleteAsync(string name, CancellationToken ct = default) =>
        await (await InnerAsync(ct).ConfigureAwait(false)).DeleteAsync(name, ct).ConfigureAwait(false);

    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default) =>
        await (await InnerAsync(ct).ConfigureAwait(false)).ListNamesAsync(ct).ConfigureAwait(false);

    private async Task<KeyValueSecretVault> InnerAsync(CancellationToken ct)
    {
        if (_inner is { } ready) return ready;
        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try { return await EnsureInitializedLockedAsync(ct).ConfigureAwait(false); }
        finally { _initLock.Release(); }
    }

    private async Task<KeyValueSecretVault> EnsureInitializedLockedAsync(CancellationToken ct)
    {
        if (_inner is { } ready) return ready;
        var envelope = await LoadEnvelopeAsync(ct).ConfigureAwait(false);
        byte[] dek;
        try { dek = envelope.UnwrapWithMachine(machineProtector); }
        catch (CryptographicException ex)
        {
            throw new SecretRecoveryRequiredException(
                "The secret vault's key was sealed on a different machine (or the platform key store was reset). " +
                "Call RecoverAsync with the recovery key to re-bind it to this host.", ex);
        }
        return _inner = BuildInner(dek);
    }

    private async Task<SecretKeyEnvelope> LoadEnvelopeAsync(CancellationToken ct)
    {
        var json = await kv.GetAsync(EnvelopeKey, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException(
                "No secret-key envelope exists yet — call GenerateMasterKeyAsync once to create the master key.");
        return SecretKeyEnvelope.FromJson(json);
    }

    private KeyValueSecretVault BuildInner(byte[] dek)
    {
        // AesGcmSecretProtector CLONES the DEK, so our unwrapped copy is now redundant — scrub it rather
        // than leave the plaintext master key lingering on the managed heap until GC (the transient recovery
        // KEK is already zeroed inside the envelope; this closes the same window for the DEK). Every unwrap
        // path (Generate/Recover/EnsureInitialized) funnels through here, so this covers all of them.
        var inner = new KeyValueSecretVault(kv, new AesGcmSecretProtector(dek), policy);
        CryptographicOperations.ZeroMemory(dek);
        return inner;
    }
}
