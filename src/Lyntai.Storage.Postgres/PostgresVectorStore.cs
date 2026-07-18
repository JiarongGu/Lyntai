using System.Globalization;
using Dapper;
using Lyntai.Memory;

namespace Lyntai.Storage.Postgres;

/// <summary>
/// pgvector-backed <see cref="IVectorStore"/> — persistent semantic-memory vectors with the similarity
/// search done IN THE DATABASE via pgvector's cosine-distance operator (<c>&lt;=&gt;</c>) and a SQL
/// <c>ORDER BY … LIMIT k</c> (only the k nearest rows are returned — not every row loaded into the app, as
/// the brute-force in-memory/SQLite stores do). Register with <c>UsePostgresVectorStore()</c>.
/// <para>Its schema (the <c>vector</c> extension + <c>lyntai_vector</c> table) is created LAZILY on first
/// use, NOT by the <c>UsePostgresStorage</c> migration — so wiring Postgres storage doesn't force pgvector
/// on consumers who don't use semantic memory. Needs rights to <c>CREATE EXTENSION vector</c> (or have a
/// DBA enable it once). The column is an unbounded <c>vector</c> (dimension-agnostic) and unindexed: the
/// search is exact (a sequential scan with pgvector's operator). An ANN index (hnsw/ivfflat, needs a fixed
/// dimension) is a future enhancement.</para>
/// </summary>
public sealed class PostgresVectorStore(IDbConnectionFactory factory) : IVectorStore
{
    private readonly object _lock = new();
    private Task? _schema;

    public async Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
    {
        await EnsureSchemaAsync().ConfigureAwait(false);
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_vector (collection, vec_id, embedding, payload)
            VALUES (@collection, @id, CAST(@embedding AS vector), @payload)
            ON CONFLICT (collection, vec_id) DO UPDATE SET embedding = CAST(@embedding AS vector), payload = @payload
            """, new { collection, id, embedding = Literal(vector), payload }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default)
    {
        if (k <= 0) return [];
        await EnsureSchemaAsync().ConfigureAwait(false);
        using var conn = factory.Open();
        // <=> is cosine DISTANCE (0 = identical); score = 1 - distance = cosine similarity, matching the
        // other IVectorStore impls. ORDER BY distance ASC + LIMIT does the top-k in the DB.
        var rows = await conn.QueryAsync<Row>(new CommandDefinition("""
            SELECT vec_id, payload, (1 - (embedding <=> CAST(@query AS vector)))::double precision AS score
            FROM lyntai_vector WHERE collection = @collection
            ORDER BY embedding <=> CAST(@query AS vector) LIMIT @k
            """, new { collection, query = Literal(query), k }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => new VectorMatch(r.VecId, r.Payload, r.Score))];
    }

    public async Task RemoveCollectionAsync(string collection, CancellationToken ct = default)
    {
        await EnsureSchemaAsync().ConfigureAwait(false);
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_vector WHERE collection = @collection", new { collection }, cancellationToken: ct)).ConfigureAwait(false);
    }

    // create the extension + table once (idempotent); a faulted setup surfaces the pgvector error on use
    private Task EnsureSchemaAsync()
    {
        if (_schema is not null) return _schema;
        lock (_lock) return _schema ??= CreateSchemaAsync();
    }

    private async Task CreateSchemaAsync()
    {
        using var conn = factory.Open();
        await conn.ExecuteAsync("CREATE EXTENSION IF NOT EXISTS vector").ConfigureAwait(false);
        await conn.ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS lyntai_vector (
                collection TEXT NOT NULL,
                vec_id     TEXT NOT NULL,
                embedding  vector NOT NULL,
                payload    TEXT NOT NULL,
                PRIMARY KEY (collection, vec_id)
            )
            """).ConfigureAwait(false);
    }

    // pgvector text literal: [1.5,2,3] (invariant, round-trippable)
    private static string Literal(float[] v) =>
        "[" + string.Join(",", v.Select(f => f.ToString("R", CultureInfo.InvariantCulture))) + "]";

    private sealed class Row
    {
        public string VecId { get; set; } = "";
        public string Payload { get; set; } = "";
        public double Score { get; set; }
    }
}
