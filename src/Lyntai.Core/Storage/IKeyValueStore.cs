namespace Lyntai.Storage;

/// <summary>App config strings by key: prompt overrides (<c>lyntai.prompt.*</c>), model routing, flags.</summary>
public interface IKeyValueStore
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);

    Task SetAsync(string key, string value, CancellationToken ct = default);

    Task DeleteAsync(string key, CancellationToken ct = default);
}
