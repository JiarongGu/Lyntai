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

    [SupportedOSPlatform("windows")]
    public string Protect(string plaintext)
    {
        EnsureWindows();
        var blob = ProtectedData.Protect(Encoding.UTF8.GetBytes(plaintext), _entropy, Map(_scope));
        return Convert.ToBase64String(blob);
    }

    [SupportedOSPlatform("windows")]
    public string Unprotect(string ciphertext)
    {
        EnsureWindows();
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

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("DpapiSecretProtector requires Windows (DPAPI).");
    }
}
