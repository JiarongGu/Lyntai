using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Lyntai.Storage;

namespace Lyntai.Secrets;

/// <summary>In-memory <see cref="ISecretVault"/> (protector applied, so it can still encrypt) — for tests
/// and ephemeral use.</summary>
public sealed class InMemorySecretVault(ISecretProtector? protector = null, ISecretAccessPolicy? policy = null) : ISecretVault
{
    private readonly ISecretProtector _protector = protector ?? new NullSecretProtector();
    private readonly ConcurrentDictionary<string, string> _store = new();

    public async Task<string?> GetAsync(string name, string? accessor = null, CancellationToken ct = default)
    {
        await GateAsync(name, accessor, ct).ConfigureAwait(false);
        return _store.TryGetValue(name, out var v) ? _protector.Unprotect(v) : null;
    }

    public Task SetAsync(string name, string value, CancellationToken ct = default)
    {
        _store[name] = _protector.Protect(value);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string name, CancellationToken ct = default)
    {
        _store.TryRemove(name, out _);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<string>>([.. _store.Keys]);

    private async Task GateAsync(string name, string? accessor, CancellationToken ct)
    {
        if (policy is not null && !await policy.CanReadAsync(name, accessor, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException($"access to secret '{name}' denied");
    }
}

/// <summary>Persistent <see cref="ISecretVault"/> over an <see cref="IKeyValueStore"/> — values encrypted
/// at rest by the protector, one key per secret under a <c>lyntai:secret:</c> prefix.
/// <see cref="ListNamesAsync"/> enumerates those keys, in ordinal order, so it lists exactly what is
/// stored.
/// <para><b>Each secret is bound to its name.</b> A write stores <c>lyntai2:</c> followed by the protector's
/// output for a header and the value, the header being <c>lyntai2:</c>, the name's length in UTF-16 code units,
/// <c>:</c> and the name. A read of a marked blob refuses one whose header names another secret, so a blob copied
/// over another name throws <see cref="CryptographicException"/> rather than decrypting where the access policy
/// would have refused it. An unmarked blob is the unbound format earlier builds wrote: it still reads, unless
/// <see cref="RefuseUnboundSecrets"/> is set, and <see cref="RebindAsync"/> rewrites it bound. The plaintext
/// <see cref="NullSecretProtector"/> stores the value as given and binds nothing.</para>
/// <para><b>A build that predates binding cannot read a bound blob</b>: once this one has written to a store, do
/// not roll back or run an older build over the same store.</para></summary>
public sealed class KeyValueSecretVault(IKeyValueStore kv, ISecretProtector protector, ISecretAccessPolicy? policy = null) : ISecretVault
{
    private const string Prefix = "lyntai:secret:";
    private const string Marker = "lyntai2:";

    /// <summary>Refuse, with a <see cref="CryptographicException"/>, a secret still in the unbound format, which
    /// anyone able to write the store could have copied from another name. Set it once <see cref="RebindAsync"/>
    /// has run; the rebind itself still reads them. It does not apply under the plaintext protector, which binds
    /// nothing.</summary>
    public bool RefuseUnboundSecrets { get; init; }

    // the plaintext protector keeps its at-rest format: a dev vault whose store reads back the value as given
    private bool Binds => protector is not NullSecretProtector;

    public async Task<string?> GetAsync(string name, string? accessor = null, CancellationToken ct = default)
    {
        if (policy is not null && !await policy.CanReadAsync(name, accessor, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException($"access to secret '{name}' denied");
        var stored = await kv.GetAsync(Prefix + name, ct).ConfigureAwait(false);
        return stored is null ? null : Open(name, stored, RefuseUnboundSecrets);
    }

    public Task SetAsync(string name, string value, CancellationToken ct = default) =>
        kv.SetAsync(Prefix + name, Seal(name, value), ct);

    public Task DeleteAsync(string name, CancellationToken ct = default) => kv.DeleteAsync(Prefix + name, ct);

    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default) =>
        [.. (await kv.ListKeysAsync(Prefix, ct).ConfigureAwait(false)).Select(k => k[Prefix.Length..])];

    /// <summary>Rewrite every secret still in the unbound format bound to its name, and return how many it
    /// rewrote; a bound one is left as it is, so a second run rewrites none. Run it once after upgrading, while
    /// nothing else writes the vault: a write landing between its read of a secret and its rewrite is lost. A blob
    /// that does not decrypt stops the pass with a <see cref="CryptographicException"/>, the ones before it already
    /// rewritten. Under the plaintext protector it rewrites nothing.</summary>
    public async Task<int> RebindAsync(CancellationToken ct = default)
    {
        if (!Binds) return 0;
        var rewritten = 0;
        foreach (var key in await kv.ListKeysAsync(Prefix, ct).ConfigureAwait(false))
        {
            var stored = await kv.GetAsync(key, ct).ConfigureAwait(false);
            if (stored is null || stored.StartsWith(Marker, StringComparison.Ordinal)) continue;
            var name = key[Prefix.Length..];
            await kv.SetAsync(key, Seal(name, Open(name, stored, refuseUnbound: false)), ct).ConfigureAwait(false);
            rewritten++;
        }
        return rewritten;
    }

    private string Seal(string name, string value)
    {
        if (!Binds) return protector.Protect(value);
        var bound = Normalize(name);
        return Marker + protector.Protect(
            string.Create(CultureInfo.InvariantCulture, $"{Marker}{bound.Length}:{bound}{value}"));
    }

    private string Open(string name, string stored, bool refuseUnbound)
    {
        if (!Binds) return protector.Unprotect(stored);
        if (stored.StartsWith(Marker, StringComparison.Ordinal))
        {
            if (!TryUnbind(protector.Unprotect(stored[Marker.Length..]), out var boundName, out var value)
                || !string.Equals(boundName, Normalize(name), StringComparison.Ordinal))
                throw new CryptographicException(
                    $"The secret stored as '{name}' is bound to another name: it was copied there, not written there.");
            return value;
        }
        if (refuseUnbound)
            throw new CryptographicException(
                $"The secret '{name}' is in the unbound format, which this vault refuses; run RebindAsync to bind it.");
        var plaintext = protector.Unprotect(stored);
        // a bound blob whose marker was cut off decrypts to its header: never hand that back as the value
        if (TryUnbind(plaintext, out _, out _))
            throw new CryptographicException($"The secret '{name}' is a bound blob stripped of its marker.");
        return plaintext;
    }

    private static bool TryUnbind(string plaintext, out string name, out string value)
    {
        (name, value) = ("", "");
        if (!plaintext.StartsWith(Marker, StringComparison.Ordinal)) return false;
        var colon = plaintext.IndexOf(':', Marker.Length);
        if (colon < 0
            || !int.TryParse(plaintext.AsSpan(Marker.Length, colon - Marker.Length), NumberStyles.None,
                CultureInfo.InvariantCulture, out var length)
            || length > plaintext.Length - colon - 1)
            return false;
        name = plaintext.Substring(colon + 1, length);
        value = plaintext[(colon + 1 + length)..];
        return true;
    }

    // as a protector's UTF-8 and every store's key read it back (docs/DECISIONS.md D262): a lone surrogate becomes U+FFFD
    private static string Normalize(string name) =>
        name.AsSpan().IndexOfAnyInRange('\uD800', '\uDFFF') < 0 ? name : Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(name));
}
