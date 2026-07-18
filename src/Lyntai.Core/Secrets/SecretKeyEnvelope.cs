using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Secrets;

/// <summary>
/// A DEK-envelope: a random 256-bit data-encryption key (DEK) that all secrets are encrypted under,
/// itself wrapped TWO ways so the vault survives both normal operation and a lost machine:
/// <list type="bullet">
///   <item><b>machine wrap</b> — the DEK sealed by an <see cref="ISecretProtector"/> bound to this host
///     (e.g. a DPAPI protector). The fast path: on the same machine the DEK unwraps with no passphrase.</item>
///   <item><b>recovery wrap</b> — the DEK sealed under a KEK derived (PBKDF2-SHA256) from a one-time
///     recovery key shown to the operator at creation. The portability path: on a NEW machine (or after a
///     DPAPI reset) the operator supplies the recovery key to unwrap, then re-binds to the new machine.</item>
/// </list>
/// The envelope carries a <see cref="MachineFingerprint"/> so a caller can tell BEFORE attempting an
/// unwrap that it was created elsewhere. This type is portable (no platform crypto) — the machine binding
/// is entirely delegated to the injected <see cref="ISecretProtector"/>, so it works with DPAPI on Windows
/// or an AES-GCM/BYO-key protector anywhere. Serialize with <see cref="ToJson"/>; persist the JSON, never
/// the DEK or the recovery key.
/// </summary>
public sealed record SecretKeyEnvelope
{
    /// <summary>PBKDF2 iteration count for the recovery KEK — OWASP-tier for SHA-256.</summary>
    public const int DefaultRecoveryIterations = 210_000;

    /// <summary>Hard floor for the recovery KDF: an envelope claiming fewer iterations is rejected rather
    /// than run with a downgraded KDF (a tampered/portable envelope can't weaken the key derivation).</summary>
    public const int MinRecoveryIterations = 100_000;

    /// <summary>The newest envelope format this build can read. A higher <see cref="Version"/> is rejected
    /// (a future format must not be silently misparsed as this one).</summary>
    public const int CurrentVersion = 1;

    private const int DekLen = 32;         // AES-256
    private const int RecoverySaltLen = 16;
    private const int RecoveryKeyBytes = 24; // 192 bits of recovery entropy → 32 base64 chars

    /// <summary>The DEK sealed by the machine protector (base64(DEK) run through <c>Protect</c>).</summary>
    public required string MachineWrappedDek { get; init; }

    /// <summary>The DEK sealed under the PBKDF2 recovery KEK (AES-256-GCM, base64(nonce|tag|ct)).</summary>
    public required string RecoveryWrappedDek { get; init; }

    /// <summary>The PBKDF2 salt for the recovery KEK (base64).</summary>
    public required string RecoverySalt { get; init; }

    /// <summary>The PBKDF2 iteration count used for the recovery KEK.</summary>
    public required int RecoveryIterations { get; init; }

    /// <summary>A stable, non-secret fingerprint of the host that produced the machine wrap.</summary>
    public required string MachineFingerprint { get; init; }

    /// <summary>Envelope format version (for forward migration).</summary>
    public int Version { get; init; } = 1;

    /// <summary>Mint a fresh envelope: generate a random DEK + recovery key, wrap the DEK under both the
    /// <paramref name="machineProtector"/> and a KEK derived from the recovery key. Returns the envelope to
    /// persist, the raw DEK to build the vault's protector, and the recovery key to show the operator ONCE
    /// (Lyntai never stores it).</summary>
    public static (SecretKeyEnvelope Envelope, byte[] Dek, string RecoveryKey) Create(
        ISecretProtector machineProtector, int recoveryIterations = DefaultRecoveryIterations)
    {
        ArgumentNullException.ThrowIfNull(machineProtector);
        var dek = RandomNumberGenerator.GetBytes(DekLen);
        var recoveryKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(RecoveryKeyBytes));
        var salt = RandomNumberGenerator.GetBytes(RecoverySaltLen);

        var envelope = new SecretKeyEnvelope
        {
            MachineWrappedDek = WrapWithMachine(dek, machineProtector),
            RecoveryWrappedDek = WrapWithRecovery(dek, recoveryKey, salt, recoveryIterations),
            RecoverySalt = Convert.ToBase64String(salt),
            RecoveryIterations = recoveryIterations,
            MachineFingerprint = ComputeMachineFingerprint(),
        };
        return (envelope, dek, recoveryKey);
    }

    /// <summary>Unwrap the DEK via the machine wrap — the fast path on the machine that created it. Throws
    /// <see cref="CryptographicException"/> if the protector can't unseal it (different machine, rotated
    /// DPAPI keys, or tampering) — the signal to fall back to <see cref="UnwrapWithRecoveryKey"/>.</summary>
    public byte[] UnwrapWithMachine(ISecretProtector machineProtector)
    {
        ArgumentNullException.ThrowIfNull(machineProtector);
        return DecodeDek(machineProtector.Unprotect(MachineWrappedDek));
    }

    /// <summary>Unwrap the DEK via the recovery key — the portability path on a new machine. Throws
    /// <see cref="CryptographicException"/> on a wrong key or a tampered wrap.</summary>
    public byte[] UnwrapWithRecoveryKey(string recoveryKey)
    {
        if (string.IsNullOrEmpty(recoveryKey)) throw new CryptographicException("Recovery key is empty.");
        byte[] salt;
        try { salt = Convert.FromBase64String(RecoverySalt); }
        catch (FormatException ex) { throw new CryptographicException("Envelope recovery salt is corrupt.", ex); }
        var kek = DeriveRecoveryKek(recoveryKey, salt, RecoveryIterations);
        // AesGcmSecretProtector already maps every unusable blob (bad key ⇒ tag mismatch, corrupt base64,
        // truncation) to CryptographicException — reuse it rather than re-implement the GCM framing
        try { return DecodeDek(new AesGcmSecretProtector(kek).Unprotect(RecoveryWrappedDek)); }
        finally { CryptographicOperations.ZeroMemory(kek); } // scrub the transient KEK (defense-in-depth)
    }

    /// <summary>Produce a new envelope that re-seals <paramref name="dek"/> for THIS machine (fresh machine
    /// wrap + fingerprint) while preserving the existing recovery wrap. Used by recovery to re-bind a DEK
    /// that was unwrapped via the recovery key to the current host, so subsequent reads take the fast
    /// path.</summary>
    public SecretKeyEnvelope RewrapForMachine(byte[] dek, ISecretProtector machineProtector)
    {
        ArgumentNullException.ThrowIfNull(machineProtector);
        if (dek.Length != DekLen) throw new ArgumentException($"DEK must be {DekLen} bytes.", nameof(dek));
        return this with
        {
            MachineWrappedDek = WrapWithMachine(dek, machineProtector),
            MachineFingerprint = ComputeMachineFingerprint(),
        };
    }

    /// <summary>True when this envelope's machine wrap was produced on the host running now.</summary>
    public bool MatchesThisMachine => MachineFingerprint == ComputeMachineFingerprint();

    // hand-rolled JsonObject (not the reflection serializer) so the envelope stays trim/AOT-clean
    public string ToJson() => new JsonObject
    {
        ["version"] = Version,
        ["machineWrappedDek"] = MachineWrappedDek,
        ["recoveryWrappedDek"] = RecoveryWrappedDek,
        ["recoverySalt"] = RecoverySalt,
        ["recoveryIterations"] = RecoveryIterations,
        ["machineFingerprint"] = MachineFingerprint,
    }.ToJsonString();

    public static SecretKeyEnvelope FromJson(string json)
    {
        JsonObject obj;
        try { obj = JsonNode.Parse(json) as JsonObject ?? throw new CryptographicException("Secret-key envelope JSON is not an object."); }
        catch (JsonException ex) { throw new CryptographicException("Secret-key envelope JSON is malformed.", ex); }
        try
        {
            var version = obj["version"]?.GetValue<int>() ?? 1;
            if (version > CurrentVersion)
                throw new CryptographicException(
                    $"Secret-key envelope version {version} is newer than this build supports ({CurrentVersion}) — upgrade Lyntai to open it.");
            var iterations = obj["recoveryIterations"]!.GetValue<int>();
            if (iterations < MinRecoveryIterations)
                throw new CryptographicException(
                    $"Secret-key envelope recovery KDF iterations ({iterations}) is below the {MinRecoveryIterations} floor — corrupt or downgraded.");
            return new SecretKeyEnvelope
            {
                Version = version,
                MachineWrappedDek = obj["machineWrappedDek"]!.GetValue<string>(),
                RecoveryWrappedDek = obj["recoveryWrappedDek"]!.GetValue<string>(),
                RecoverySalt = obj["recoverySalt"]!.GetValue<string>(),
                RecoveryIterations = iterations,
                MachineFingerprint = obj["machineFingerprint"]!.GetValue<string>(),
            };
        }
        catch (Exception ex) when (ex is NullReferenceException or InvalidOperationException or FormatException)
        {
            throw new CryptographicException("Secret-key envelope JSON is missing required fields.", ex);
        }
    }

    /// <summary>A stable, non-secret host fingerprint (SHA-256 hex of machine name + OS + architecture).
    /// Used only to hint whether the machine wrap is unsealable here — never for key material.</summary>
    public static string ComputeMachineFingerprint()
    {
        var material = string.Join('|',
            Environment.MachineName,
            Environment.OSVersion.Platform.ToString(),
            System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString());
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)));
    }

    private static string WrapWithMachine(byte[] dek, ISecretProtector machineProtector) =>
        machineProtector.Protect(Convert.ToBase64String(dek));

    private static string WrapWithRecovery(byte[] dek, string recoveryKey, byte[] salt, int iterations)
    {
        var kek = DeriveRecoveryKek(recoveryKey, salt, iterations);
        try { return new AesGcmSecretProtector(kek).Protect(Convert.ToBase64String(dek)); }
        finally { CryptographicOperations.ZeroMemory(kek); } // scrub the transient KEK (defense-in-depth)
    }

    private static byte[] DeriveRecoveryKek(string recoveryKey, byte[] salt, int iterations)
    {
        // the KDF choke point — floor the iteration count here too, so an envelope constructed directly
        // (bypassing FromJson) with a downgraded/zero count fails as CryptographicException, not a stray
        // ArgumentOutOfRangeException
        if (iterations < MinRecoveryIterations)
            throw new CryptographicException(
                $"Recovery KDF iteration count ({iterations}) is below the {MinRecoveryIterations} floor — envelope corrupt or downgraded.");
        return Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(recoveryKey), salt, iterations, HashAlgorithmName.SHA256, DekLen);
    }

    private static byte[] DecodeDek(string base64Dek)
    {
        byte[] dek;
        try { dek = Convert.FromBase64String(base64Dek); }
        catch (FormatException ex) { throw new CryptographicException("Unwrapped DEK is not valid base64 — envelope corrupt.", ex); }
        if (dek.Length != DekLen) throw new CryptographicException($"Unwrapped DEK is {dek.Length} bytes, expected {DekLen} — envelope corrupt.");
        return dek;
    }
}

/// <summary>Thrown when an <see cref="EnvelopeSecretVault"/> holds an envelope whose machine wrap can't be
/// unsealed on this host (created on a different machine, or the platform key store was reset). The vault
/// can't decrypt secrets until <see cref="EnvelopeSecretVault.RecoverAsync"/> is called with the recovery
/// key.</summary>
public sealed class SecretRecoveryRequiredException(string message, Exception? inner = null)
    : Exception(message, inner);
