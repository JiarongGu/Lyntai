using System.Collections.Concurrent;
using Lyntai.Storage;

namespace Lyntai.Tests.Fakes;

public sealed class InMemoryKeyValueStore : IKeyValueStore
{
    public ConcurrentDictionary<string, string> Data { get; } = new();

    public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(Data.TryGetValue(key, out var v) ? v : null);

    public Task SetAsync(string key, string value, CancellationToken ct = default)
    {
        Data[key] = value;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct = default)
    {
        Data.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
