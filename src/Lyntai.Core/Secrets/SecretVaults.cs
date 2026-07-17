using System.Collections.Concurrent;
using System.Text.Json.Nodes;
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
/// at rest by the protector under a <c>lyntai:secret:</c> prefix, with a small index entry backing
/// <see cref="ListNamesAsync"/> (index writes serialized per-process; the KV isn't a transaction, so
/// heavy concurrent secret writes could race the index — acceptable for its use).</summary>
public sealed class KeyValueSecretVault(IKeyValueStore kv, ISecretProtector protector, ISecretAccessPolicy? policy = null) : ISecretVault
{
    private const string Prefix = "lyntai:secret:";
    // deliberately OUTSIDE the Prefix namespace ("lyntai:secret-" has a dash where a secret key has a
    // colon) so no caller-chosen secret name can ever map onto the index key and corrupt it
    private const string IndexKey = "lyntai:secret-names";
    private readonly SemaphoreSlim _indexLock = new(1, 1);

    public async Task<string?> GetAsync(string name, string? accessor = null, CancellationToken ct = default)
    {
        if (policy is not null && !await policy.CanReadAsync(name, accessor, ct).ConfigureAwait(false))
            throw new UnauthorizedAccessException($"access to secret '{name}' denied");
        var stored = await kv.GetAsync(Prefix + name, ct).ConfigureAwait(false);
        return stored is null ? null : protector.Unprotect(stored);
    }

    public async Task SetAsync(string name, string value, CancellationToken ct = default)
    {
        await kv.SetAsync(Prefix + name, protector.Protect(value), ct).ConfigureAwait(false);
        await UpdateIndexAsync(names => names.Add(name), ct).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string name, CancellationToken ct = default)
    {
        await kv.DeleteAsync(Prefix + name, ct).ConfigureAwait(false);
        await UpdateIndexAsync(names => names.Remove(name), ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<string>> ListNamesAsync(CancellationToken ct = default) =>
        [.. await ReadIndexAsync(ct).ConfigureAwait(false)];

    private async Task<HashSet<string>> ReadIndexAsync(CancellationToken ct)
    {
        var raw = await kv.GetAsync(IndexKey, ct).ConfigureAwait(false);
        if (raw is null) return new(StringComparer.Ordinal);
        try
        {
            return JsonNode.Parse(raw) is JsonArray arr
                ? [.. arr.Where(n => n is not null).Select(n => n!.GetValue<string>())]
                : new(StringComparer.Ordinal);
        }
        catch (System.Text.Json.JsonException) { return new(StringComparer.Ordinal); }
    }

    private async Task UpdateIndexAsync(Action<HashSet<string>> mutate, CancellationToken ct)
    {
        await _indexLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var names = await ReadIndexAsync(ct).ConfigureAwait(false);
            mutate(names);
            var arr = new JsonArray([.. names.Select(n => (JsonNode)JsonValue.Create(n))]);
            await kv.SetAsync(IndexKey, arr.ToJsonString(), ct).ConfigureAwait(false);
        }
        finally { _indexLock.Release(); }
    }
}
