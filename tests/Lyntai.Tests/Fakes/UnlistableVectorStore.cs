using Lyntai.Memory;

namespace Lyntai.Tests.Fakes;

/// <summary>A BYO vector store with only the required half of the seam: it forwards every
/// <see cref="IVectorStore"/> member to a real in-process store and deliberately does NOT implement
/// <see cref="IListableVectorStore"/> — the shape every cross-scope fallback exists for.</summary>
internal sealed class UnlistableVectorStore : IVectorStore
{
    private readonly InMemoryVectorStore _inner = new();

    public Task UpsertAsync(string collection, string id, float[] vector, string payload,
        CancellationToken ct = default) => _inner.UpsertAsync(collection, id, vector, payload, ct);

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k,
        CancellationToken ct = default) => _inner.SearchAsync(collection, query, k, ct);

    public Task DeleteAsync(string collection, string id, CancellationToken ct = default) =>
        _inner.DeleteAsync(collection, id, ct);

    public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) =>
        _inner.RemoveCollectionAsync(collection, ct);
}
