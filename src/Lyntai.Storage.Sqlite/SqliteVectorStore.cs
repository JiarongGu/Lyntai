using Dapper;
using Lyntai.Memory;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IVectorStore"/> — persistent semantic-memory vectors (the in-memory default in
/// Core is lost on restart). Search is brute-force exact cosine: the collection's rows are loaded and
/// ranked in-process (SQLite has no native vector ops), so it's persistent but not indexed — fine for up to
/// some thousands of vectors per collection; for larger corpora use a dedicated vector backend (pgvector).
/// Vectors are stored as a JSON float array. Register with <c>UseSqliteVectorStore()</c>.
/// </summary>
public sealed class SqliteVectorStore(IDbConnectionFactory factory) : IVectorStore
{
    public async Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_vector (collection, vec_id, vector, payload)
            VALUES (@collection, @id, @vector, @payload)
            ON CONFLICT(collection, vec_id) DO UPDATE SET vector = @vector, payload = @payload
            """, new { collection, id, vector = SqliteJson.Serialize(vector), payload }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default)
    {
        if (k <= 0) return [];
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            "SELECT vec_id, vector, payload FROM lyntai_vector WHERE collection = @collection",
            new { collection }, cancellationToken: ct)).ConfigureAwait(false);

        // ThenBy is load-bearing, not tidiness: OrderByDescending is a STABLE sort, so without it tied scores
        // keep whatever order the scan yielded and an arbitrary member of the tie drops out at the k boundary.
        // The SELECT above has no ORDER BY, so that order is the query PLAN's — today it walks the
        // (collection, vec_id) primary-key autoindex and happens to come back ascending, which is why the
        // contract's tie facts passed here before this line existed. Nobody chose that; a rewrite, an ANALYZE
        // or a different plan changes it silently. `VectorStoreContract.Equal_scores_are_ordered_by_id`.
        return [.. rows
            .Select(r => new VectorMatch(r.VecId, r.Payload, VectorMath.Cosine(query, SqliteJson.Deserialize<float[]>(r.Vector) ?? [])))
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Take(k)];
    }

    public async Task DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_vector WHERE collection = @collection AND vec_id = @id",
            new { collection, id }, cancellationToken: ct)).ConfigureAwait(false); // no-op if absent
    }

    public async Task RemoveCollectionAsync(string collection, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_vector WHERE collection = @collection", new { collection }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private sealed class Row
    {
        public string VecId { get; set; } = "";
        public string Vector { get; set; } = "";
        public string Payload { get; set; } = "";
    }
}
