namespace Lyntai.Storage;

/// <summary>App config strings by key: prompt overrides (<c>lyntai.prompt.*</c>), model routing, flags.</summary>
public interface IKeyValueStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string value, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);

    /// <summary>Every key, or only those starting with <paramref name="prefix"/> (case-sensitive
    /// starts-with; null = all), in ordinal order. This is what lets a namespaced consumer (a vault, a
    /// prompt registry, a settings page) enumerate its own <c>ns.*</c> slice without maintaining a
    /// separate index.</summary>
    Task<IReadOnlyList<string>> ListKeysAsync(string? prefix = null, CancellationToken ct = default);
}
