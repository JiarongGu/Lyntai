using Lyntai.Memory;

namespace Lyntai.Tests.Fakes;

/// <summary>An in-memory index whose next upsert parks until released — a write step held open, so a test can
/// land a removal while it is in flight.</summary>
internal sealed class ParkingVectorStore : IReadableVectorStore
{
    private readonly InMemoryVectorStore _inner = new();
    private int _parkNext;

    public TaskCompletionSource Parked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ParkNextUpsert() => Volatile.Write(ref _parkNext, 1);

    public async Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _parkNext, 0) == 1)
        {
            Parked.SetResult();
            await Release.Task;
        }
        await _inner.UpsertAsync(collection, id, vector, payload, ct);
    }

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
        _inner.SearchAsync(collection, query, k, ct);

    public Task DeleteAsync(string collection, string id, CancellationToken ct = default) => _inner.DeleteAsync(collection, id, ct);

    public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) =>
        _inner.RemoveCollectionAsync(collection, ct);

    public Task<IReadOnlyList<VectorEntry>> GetAsync(string collection, IReadOnlyCollection<string> ids,
        CancellationToken ct = default) => _inner.GetAsync(collection, ids, ct);
}
