using Dapper;
using Lyntai.Storage.Relational;
using Lyntai.Memory;

namespace Lyntai.Storage.Sqlite;

/// <summary>
/// SQLite-backed <see cref="IVectorStore"/> — persistent semantic-memory vectors (the in-memory default in
/// Core is lost on restart). Search is brute-force exact cosine: the collection's rows are loaded and
/// ranked in-process (SQLite has no native vector ops), so it's persistent but not indexed — fine for up to
/// some thousands of vectors per collection; for larger corpora use a dedicated vector backend (pgvector).
/// Vectors are stored as a JSON float array. Register with <c>UseSqliteVectorStore()</c>.
/// </summary>
public sealed class SqliteVectorStore(IDbConnectionFactory factory) : IListableVectorStore, IReadableVectorStore
{
    public async Task UpsertAsync(string collection, string id, float[] vector, string payload, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_vector (collection, vec_id, vector, payload)
            VALUES (@collection, @id, @vector, @payload)
            ON CONFLICT(collection, vec_id) DO UPDATE SET vector = @vector, payload = @payload
            """, new { collection, id, vector = ReflectionJson.Serialize(vector), payload }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<VectorMatch>> SearchAsync(string collection, float[] query, int k, CancellationToken ct = default) =>
        SearchCoreAsync(collection, query, k, filter: null, ct);

    /// <inheritdoc />
    /// <remarks>Filters in the query, so an excluded row is never parsed; the id sets travel as JSON arrays through
    /// <c>json_each</c>, as <see cref="GetAsync"/>'s do, for the same bound-parameter reason.</remarks>
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
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // the fragments are compile-time constants; every value is a parameter
        var sql = "SELECT vec_id, vector, payload FROM lyntai_vector WHERE collection = @collection"
            + (filter?.Ids is null ? "" : " AND vec_id IN (SELECT value FROM json_each(@ids))")
            + (filter?.ExcludeIds is null ? "" : " AND vec_id NOT IN (SELECT value FROM json_each(@exclude))");
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(sql, new
        {
            collection,
            ids = filter?.Ids is { } ids ? ReflectionJson.Serialize(ids.ToArray()) : null,
            exclude = filter?.ExcludeIds is { } exclude ? ReflectionJson.Serialize(exclude.ToArray()) : null,
        }, cancellationToken: ct)).ConfigureAwait(false);

        // ThenBy is load-bearing: the SELECT has no ORDER BY, so without it tied scores keep the query PLAN's
        // order and an arbitrary member of the tie drops out at the k boundary (VectorStoreContract).
        return [.. rows
            .Select(r => new VectorMatch(r.VecId, r.Payload, VectorMath.Cosine(query, ReflectionJson.Deserialize<float[]>(r.Vector) ?? [])))
            .OrderByDescending(m => m.Score)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .Take(k)];
    }

    /// <inheritdoc />
    /// <remarks>The ids travel as ONE JSON array read through <c>json_each</c>, not as an <c>IN</c> list: Dapper
    /// expands a list into one bound parameter per id, and SQLite refuses a statement past 32,766 of them.</remarks>
    public async Task<IReadOnlyList<VectorEntry>> GetAsync(string collection, IReadOnlyCollection<string> ids,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0) return [];
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition("""
            SELECT vec_id, vector, payload FROM lyntai_vector
            WHERE collection = @collection AND vec_id IN (SELECT value FROM json_each(@ids))
            """, new { collection, ids = ReflectionJson.Serialize(ids.Distinct(StringComparer.Ordinal).ToArray()) },
            cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows
            .Select(r => new VectorEntry(r.VecId, ReflectionJson.Deserialize<float[]>(r.Vector) ?? [], r.Payload))
            .OrderBy(e => e.Id, StringComparer.Ordinal)];
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

    /// <inheritdoc />
    /// <remarks><c>substr(...) = @prefix</c>, never <c>LIKE</c>: SQLite's <c>LIKE</c> is ASCII
    /// case-INSENSITIVE by default, so a prefix match through it would reach a different task whose key
    /// differs only in case — and <c>%</c>/<c>_</c> inside a caller's prefix would be read as
    /// wildcards. <c>substr</c> compares under the column's BINARY collation, which is the ordinal
    /// comparison the contract promises, and treats the prefix as data.</remarks>
    public async Task<IReadOnlyList<string>> ListCollectionsAsync(string prefix, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(
            "SELECT DISTINCT collection FROM lyntai_vector WHERE substr(collection, 1, @len) = @prefix",
            new { len = prefix.Length, prefix }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    private sealed class Row
    {
        public string VecId { get; set; } = "";
        public string Vector { get; set; } = "";
        public string Payload { get; set; } = "";
    }
}
