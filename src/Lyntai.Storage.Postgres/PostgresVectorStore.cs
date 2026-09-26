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
public sealed class PostgresVectorStore(IDbConnectionFactory factory) : IListableVectorStore, IReadableVectorStore
{
    private readonly object _lock = new();
    private Task? _schema;

    /// <summary>Insert or replace the vector (+ its payload) at <paramref name="id"/> within
    /// <paramref name="collection"/> — an upsert on the <c>(collection, vec_id)</c> primary key, so a repeat
    /// id overwrites the prior embedding and payload rather than duplicating. Creates the pgvector schema on
    /// first use (see the type remarks). Vectors are dimension-agnostic (an unbounded <c>vector</c> column).</summary>
    public async Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
    {
        await EnsureSchemaAsync().ConfigureAwait(false);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_vector (collection, vec_id, embedding, payload)
            VALUES (@collection, @id, CAST(@embedding AS vector), @payload)
            ON CONFLICT (collection, vec_id) DO UPDATE SET embedding = CAST(@embedding AS vector), payload = @payload
            """, new { collection, id, embedding = Literal(vector), payload }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Return the <paramref name="k"/> nearest vectors in <paramref name="collection"/> to
    /// <paramref name="query"/>, most-similar first. Each match's score is COSINE SIMILARITY in
    /// <c>[-1, 1]</c> (computed as <c>1 - cosine_distance</c>, so 1 = identical direction) — matching the
    /// other <see cref="IVectorStore"/> implementations, including their rule that a stored vector of another
    /// dimension, or a zero vector, scores 0 and ranks last rather than failing the search. The search is an
    /// EXACT (brute-force) scan: the column is unindexed, so pgvector compares the query against every row in
    /// the collection (no ANN approximation). <paramref name="k"/> &lt;= 0 returns an empty list without
    /// touching the database.</summary>
    /// <returns>Up to <paramref name="k"/> matches ordered by descending similarity; empty when the
    /// collection has no rows or <paramref name="k"/> &lt;= 0.</returns>
    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
        SearchCoreAsync(collection, query, k, filter: null, ct);

    /// <inheritdoc />
    /// <remarks>Filters in SQL (<c>= ANY</c> / <c>&lt;&gt; ALL</c>), so the top-k is still taken in the
    /// database.</remarks>
    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, VectorSearchFilter filter,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return SearchCoreAsync(collection, query, k, filter, ct);
    }

    private async Task<IReadOnlyList<VectorMatch>> SearchCoreAsync(string collection, float[] query, int k,
        VectorSearchFilter? filter, CancellationToken ct)
    {
        if (k <= 0 || filter?.Ids is { Count: 0 }) return [];
        await EnsureSchemaAsync().ConfigureAwait(false);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // <=> is cosine DISTANCE (0 = identical); score = 1 - distance = cosine similarity, matching the
        // other IVectorStore impls, and the top-k is taken in the DB.
        //
        // The CASE is VectorMath.Cosine's rule: a row of another dimension or a zero vector scores 0. Left to
        // pgvector, a mismatch raises for the WHOLE statement and a zero norm scores NaN — and the column is
        // dimension-agnostic on purpose, so a collection that outlives an embedder swap holds both.
        //
        // vec_id breaks ties, COLLATE "C" so the order is byte order, as every other backend's is. The filter
        // fragments are compile-time constants; every value is a parameter.
        var filters = (filter?.Ids is null ? "" : " AND vec_id = ANY(@ids)")
            + (filter?.ExcludeIds is null ? "" : " AND vec_id <> ALL(@exclude)");
        var rows = await conn.QueryAsync<Row>(new CommandDefinition($"""
            SELECT vec_id, payload,
                   CASE WHEN vector_dims(embedding) = vector_dims(q.v)
                             AND vector_norm(embedding) > 0 AND vector_norm(q.v) > 0
                        THEN (1 - (embedding <=> q.v))::double precision
                        ELSE 0 END AS score
            FROM lyntai_vector, (SELECT CAST(@query AS vector) AS v) q
            WHERE collection = @collection{filters}
            ORDER BY score DESC, vec_id COLLATE "C" LIMIT @k
            """, new
            {
                collection, query = Literal(query), k,
                ids = filter?.Ids?.ToArray(), exclude = filter?.ExcludeIds?.ToArray(),
            }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => new VectorMatch(r.VecId, r.Payload, r.Score))];
    }

    /// <inheritdoc />
    /// <remarks>Reads <c>embedding::real[]</c>, which Npgsql hands back as the exact float4 values — no pgvector
    /// client package, and no text round trip to lose a digit.</remarks>
    public async Task<IReadOnlyList<VectorEntry>> GetAsync(string collection, IReadOnlyCollection<string> ids,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return [];
        await EnsureSchemaAsync().ConfigureAwait(false);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<EntryRow>(new CommandDefinition("""
            SELECT vec_id, embedding::real[] AS vector, payload FROM lyntai_vector
            WHERE collection = @collection AND vec_id = ANY(@ids)
            """, new { collection, ids = ids.Distinct(StringComparer.Ordinal).ToArray() },
            cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows
            .Select(r => new VectorEntry(r.VecId, r.Vector, r.Payload))
            .OrderBy(e => e.Id, StringComparer.Ordinal)];
    }

    /// <summary>Remove the single vector stored under <paramref name="id"/> within
    /// <paramref name="collection"/>. Idempotent — a missing (collection, id) is a no-op (the underlying
    /// <c>DELETE</c> matches no rows). For dropping an entire collection use <see cref="RemoveCollectionAsync"/>.</summary>
    public async Task DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        await EnsureSchemaAsync().ConfigureAwait(false);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_vector WHERE collection = @collection AND vec_id = @id",
            new { collection, id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Delete every vector in <paramref name="collection"/>. Idempotent — a missing/empty
    /// collection is a no-op (the underlying <c>DELETE</c> simply matches no rows). The table and the
    /// pgvector extension are left in place; only the collection's rows are removed.</summary>
    public async Task RemoveCollectionAsync(string collection, CancellationToken ct = default)
    {
        await EnsureSchemaAsync().ConfigureAwait(false);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_vector WHERE collection = @collection", new { collection }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks><c>substr(...) = @prefix</c> rather than <c>LIKE</c>, so a <c>%</c> or <c>_</c> inside a
    /// caller's prefix stays data — the contract says the prefix is never a pattern. <c>COLLATE "C"</c> makes
    /// the comparison byte-exact, which is the ORDINAL rule the contract promises and the same reasoning
    /// <see cref="SearchAsync(string, float[], int, CancellationToken)"/>'s tiebreak carries: a differently-collated deployment would otherwise
    /// disagree with the other two backends.</remarks>
    public async Task<IReadOnlyList<string>> ListCollectionsAsync(string prefix, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        await EnsureSchemaAsync().ConfigureAwait(false);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT collection FROM lyntai_vector
            WHERE substr(collection, 1, @len) COLLATE "C" = @prefix
            """,
            new { len = prefix.Length, prefix }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    // create the extension + table once (idempotent). If the first attempt FAULTED (a transient blip —
    // pool exhaustion, a momentary lock/permission issue on CREATE EXTENSION), don't cache the failure
    // forever: retry on the next call, so one bad moment doesn't brick the store for the process lifetime.
    private Task EnsureSchemaAsync()
    {
        var current = _schema;
        if (current is { IsFaulted: false, IsCanceled: false }) return current;
        lock (_lock)
        {
            if (_schema is null or { IsFaulted: true } or { IsCanceled: true }) _schema = CreateSchemaAsync();
            return _schema;
        }
    }

    private async Task CreateSchemaAsync()
    {
        // async open, deliberately UNLINKED from any caller token: this task is SHARED across every first
        // caller (see EnsureSchemaAsync) — one caller's cancellation must not fault the schema task the
        // others are awaiting. The retry-on-fault gate re-creates it if it genuinely fails.
        await using var conn = await factory.OpenAsync(CancellationToken.None).ConfigureAwait(false);
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

    private sealed class EntryRow
    {
        public string VecId { get; set; } = "";
        public float[] Vector { get; set; } = [];
        public string Payload { get; set; } = "";
    }
}
