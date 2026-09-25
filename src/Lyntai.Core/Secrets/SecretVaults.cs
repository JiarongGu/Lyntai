using System.Collections.Concurrent;
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
/// stored.</summary>
public sealed class KeyValueSecretVault(IKeyValueStore kv, ISecretProtector protector, ISecretAccessPolicy? policy = null) : ISecretVault
{
    private const string Prefix = "lyntai:secret:";

    public async Task<string?> GetAsync(string name, string? accessor = null, CancellationToken ct = default)
    {
        if (policy is not null && !await policy.CanReadAsync(name, accessor, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException($"access to secret '{name}' denied");
        var stored = await kv.GetAsync(Prefix + name, ct).ConfigureAwait(false);
        return stored is null ? null : protector.Unprotect(stored);
    }

    public Task SetAsync(string name, string value, CancellationToken ct = default) =>
        kv.SetAsync(Prefix + name, protector.Protect(value), ct);

    public Task DeleteAsync(string name, CancellationToken ct = default) => kv.DeleteAsync(Prefix + name, ct);

    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default) =>
        [.. (await kv.ListKeysAsync(Prefix, ct).ConfigureAwait(false)).Select(k => k[Prefix.Length..])];
}
