namespace Lyntai.Secrets;

/// <summary>
/// A named secret store (design §9: security/access-gate + secret vault). Values are protected at rest by
/// an <see cref="ISecretProtector"/> (AES-GCM when you supply a key). An optional
/// <see cref="ISecretAccessPolicy"/> gates reads — a denied read throws <see cref="UnauthorizedAccessException"/>,
/// distinct from a missing secret (null).
/// </summary>
public interface ISecretVault
{
    /// <summary>Read and decrypt a secret, or null if it doesn't exist. <paramref name="accessor"/> is the
    /// caller identity the access policy (if any) checks.</summary>
    Task<string?> GetAsync(string name, string? accessor = null, CancellationToken ct = default);

    /// <summary>Store (encrypting at rest) a secret, overwriting any existing value.</summary>
    Task SetAsync(string name, string value, CancellationToken ct = default);

    Task DeleteAsync(string name, CancellationToken ct = default);

    /// <summary>The names of stored secrets (never the values).</summary>
    Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default);
}

/// <summary>Encrypts/decrypts secret values at rest.</summary>
public interface ISecretProtector
{
    string Protect(string plaintext);

    string Unprotect(string ciphertext);
}

/// <summary>The access gate: decides whether a given accessor may READ a given secret. It gates
/// reads only — Set/Delete/ListNames are not policy-checked (mutations/enumeration are considered a
/// management concern, guarded by who can reach the vault at all).
/// <para><b>Security:</b> if your implementation compares the <c>accessor</c> (or any secret
/// token it carries) against an expected value, use a constant-time compare
/// (<see cref="System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.ReadOnlySpan{byte},System.ReadOnlySpan{byte})"/>),
/// not <c>==</c>/<c>string.Equals</c> — an early-exit compare leaks the token byte-by-byte through timing.
/// Plain identity/role checks (matching an id against a set) don't need this; secret-material equality
/// does.</para></summary>
public interface ISecretAccessPolicy
{
    /// <summary>Gate a READ of secret <paramref name="name"/> by <paramref name="accessor"/>.
    /// <para>By design this policy gates READS only — <c>Set</c>/<c>Delete</c>/<c>ListNames</c> are NOT gated
    /// by contract (writes/enumeration are assumed to be an admin/provisioning path, not the runtime read
    /// path the policy protects). If you need to gate writes/enumeration, wrap the <see cref="ISecretVault"/>
    /// with your own decorator; a first-class write/enumerate hook may be added later.</para></summary>
    Task<bool> CanReadAsync(string name, string? accessor, CancellationToken ct = default);
}
