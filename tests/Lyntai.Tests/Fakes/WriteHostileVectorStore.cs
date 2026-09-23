using Lyntai.Memory;

namespace Lyntai.Tests.Fakes;

/// <summary>A vector store that SEARCHES fine and refuses to be written to — so a write's embed and similarity
/// search succeed and the index is what fails, the half a failing vector backend can never reach.</summary>
public sealed class WriteHostileVectorStore : IVectorStore
{
    private readonly InMemoryVectorStore _inner = new();

    /// <summary>How many writes were attempted, so a test can tell a refused write from one never tried.</summary>
    public int Upserts { get; private set; }

    public Task UpsertAsync(string collection, string id, float[] vector, string payload,
        CancellationToken ct = default)
    {
        Upserts++;
        throw new InvalidOperationException("the vector store is read-only");
    }

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k,
        CancellationToken ct = default) => _inner.SearchAsync(collection, query, k, ct);

    public Task DeleteAsync(string collection, string id, CancellationToken ct = default) =>
        _inner.DeleteAsync(collection, id, ct);

    public Task RemoveCollectionAsync(string collection, CancellationToken ct = default) =>
        _inner.RemoveCollectionAsync(collection, ct);
}
