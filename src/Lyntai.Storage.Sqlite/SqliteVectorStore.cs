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
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_vector (collection, vec_id, vector, payload)
            VALUES (@collection, @id, @vector, @payload)
            ON CONFLICT(collection, vec_id) DO UPDATE SET vector = @vector, payload = @payload
            """, new { collection, id, vector = SqliteJson.Serialize(vector), payload }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default)
    {
        if (k <= 0) return [];
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            "SELECT vec_id, vector, payload FROM lyntai_vector WHERE collection = @collection",
            new { collection }, cancellationToken: ct)).ConfigureAwait(false);

        return [.. rows
            .Select(r => new VectorMatch(r.VecId, r.Payload, Cosine(query, SqliteJson.Deserialize<float[]>(r.Vector) ?? [])))
            .OrderByDescending(m => m.Score)
            .Take(k)];
    }

    public async Task RemoveCollectionAsync(string collection, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_vector WHERE collection = @collection", new { collection }, cancellationToken: ct)).ConfigureAwait(false);
    }

    // cosine similarity — kept local (Core's is internal); 0 on a dimension mismatch or a zero vector, so a
    // stray malformed row can't sink a whole search (the persistent store may outlive an embedding-model swap)
    private static double Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++)
        {
            dot += (double)a[i] * b[i];
            na += (double)a[i] * a[i];
            nb += (double)b[i] * b[i];
        }
        return na == 0 || nb == 0 ? 0 : dot / (Math.Sqrt(na) * Math.Sqrt(nb));
    }

    private sealed class Row
    {
        public string VecId { get; set; } = "";
        public string Vector { get; set; } = "";
        public string Payload { get; set; } = "";
    }
}
