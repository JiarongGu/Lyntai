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

/// <summary>The access gate: decides whether <paramref name="accessor"/> may read a given secret.</summary>
public interface ISecretAccessPolicy
{
    Task<bool> CanReadAsync(string name, string? accessor, CancellationToken ct = default);
}
