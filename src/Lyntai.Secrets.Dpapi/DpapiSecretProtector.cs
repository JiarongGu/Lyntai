using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Secrets;

/// <summary>The scope a <see cref="DpapiSecretProtector"/> binds to.</summary>
public enum DpapiScope
{
    /// <summary>Only the Windows user account that sealed the value can unseal it (roaming-profile portable).
    /// The default — least privilege.</summary>
    CurrentUser,

    /// <summary>Any account on the same machine can unseal it (for a service running as a system/managed
    /// account, or values shared across users on one host).</summary>
    LocalMachine,
}

/// <summary>
/// A Windows-DPAPI <see cref="ISecretProtector"/>: seals values with <see cref="ProtectedData"/> bound to
/// the current user (or machine), so the ciphertext can only be unsealed on the same host by the same
/// principal — no key for the app to manage or store. Output is base64(protected-blob); optional entropy
/// adds an app-supplied secondary secret to the binding.
/// <para><b>Windows-only.</b> Every operation throws <see cref="PlatformNotSupportedException"/> off
/// Windows (checked eagerly with a clear message rather than surfacing the platform P/Invoke failure). Use
/// this as the machine-binding protector for an <see cref="EnvelopeSecretVault"/> (via
/// <c>AddDpapiSecretVault</c>) so the DEK is DPAPI-sealed with a recovery key for off-machine recovery; on
/// non-Windows hosts fall back to an AES-GCM/BYO-key protector.</para>
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    private readonly DpapiScope _scope;
    private readonly byte[]? _entropy;

    /// <param name="scope">User- or machine-bound (default user).</param>
    /// <param name="entropy">Optional extra secret mixed into the binding (UTF-8), so a leaked blob can't be
    /// unsealed without it even by the right principal.</param>
    public DpapiSecretProtector(DpapiScope scope = DpapiScope.CurrentUser, string? entropy = null)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "DpapiSecretProtector requires Windows (DPAPI). On other platforms use AesGcmSecretProtector or another ISecretProtector.");
        _scope = scope;
        _entropy = entropy is null ? null : Encoding.UTF8.GetBytes(entropy);
    }

    // No per-call platform guard: the constructor already throws PlatformNotSupportedException off
    // Windows, so no instance can exist on another OS (the attributes satisfy the CA1416 analyzer).
    /// <summary>Seal <paramref name="plaintext"/> with DPAPI, returning <c>base64(protected-blob)</c> bound to
    /// the configured <see cref="DpapiScope"/> (and any entropy). Only the same principal on the same host can
    /// <see cref="Unprotect"/> it.</summary>
    /// <returns>The base64-encoded protected blob.</returns>
    [SupportedOSPlatform("windows")]
    public string Protect(string plaintext)
    {
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), _entropy, Map(_scope));
        return Convert.ToBase64String(blob);
    }

    /// <summary>Unseal a blob produced by <see cref="Protect"/>. EVERY unusable input normalizes to a single
    /// <see cref="CryptographicException"/> — the one type to catch for all at-rest failure: non-base64 input,
    /// a corrupt/tampered blob, and a blob sealed by a different user or on a different machine all surface as
    /// <see cref="CryptographicException"/> (the base64 parse failure is deliberately re-wrapped to match
    /// DPAPI's own error idiom, so callers need not special-case it).</summary>
    /// <returns>The original plaintext.</returns>
    /// <exception cref="CryptographicException"><paramref name="ciphertext"/> is not valid base64, or is
    /// corrupt/tampered, or was sealed by a different principal or on a different host.</exception>
    [SupportedOSPlatform("windows")]
    public string Unprotect(string ciphertext)
    {
        // every unusable blob must surface as CryptographicException (the one type a caller catches for all
        // at-rest corruption) — normalize the base64 parse failure to match DPAPI's own on tamper/wrong-host
        byte[] blob;
        try { blob = Convert.FromBase64String(ciphertext); }
        catch (FormatException ex)
        {
            throw new CryptographicException("DPAPI blob is not valid base64 — corrupt, or not produced by this protector.", ex);
        }
        var pt = ProtectedData.Unprotect(blob, _entropy, Map(_scope)); // throws CryptographicException on wrong host/user/tamper
        return Encoding.UTF8.GetString(pt);
    }

    [SupportedOSPlatform("windows")]
    private static DataProtectionScope Map(DpapiScope scope) =>
        scope == DpapiScope.LocalMachine ? DataProtectionScope.LocalMachine : DataProtectionScope.CurrentUser;
}
