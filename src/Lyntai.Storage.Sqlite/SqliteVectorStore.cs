using System.Data.Common;
using System.Globalization;
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

    /// <summary>A scan, and kept one: score every vector in the collection against the query, hold the best
    /// <paramref name="k"/>, and read the payload of those alone. Scoring streams each vector's JSON text
    /// straight into the dot product, so a search allocates no float array and no payload per row — at 11,000
    /// vectors the per-row materialisation, not the arithmetic, was what a search cost.</summary>
    private async Task<IReadOnlyList<VectorMatch>> SearchCoreAsync(string collection, float[] query, int k,
        VectorSearchFilter? filter, CancellationToken ct)
    {
        if (k <= 0 || filter?.Ids is { Count: 0 }) return [];
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // the fragments are compile-time constants; every value is a parameter
        await using var scan = conn.CreateCommand();
        scan.CommandText = "SELECT vec_id, vector FROM lyntai_vector WHERE collection = @collection"
            + (filter?.Ids is null ? "" : " AND vec_id IN (SELECT value FROM json_each(@ids))")
            + (filter?.ExcludeIds is null ? "" : " AND vec_id NOT IN (SELECT value FROM json_each(@exclude))");
        AddParameter(scan, "@collection", collection);
        if (filter?.Ids is { } ids) AddParameter(scan, "@ids", ReflectionJson.Serialize(ids.ToArray()));
        if (filter?.ExcludeIds is { } exclude) AddParameter(scan, "@exclude", ReflectionJson.Serialize(exclude.ToArray()));

        // The heap's root is the WORST kept entry: lowest score, and among equal scores the greatest id —
        // the exact reverse of the result order (score descending, id ascending), so a tie at the k boundary
        // keeps the same member the sorted scan would. The id tiebreak is load-bearing: the SELECT has no
        // ORDER BY, so without it tied scores keep the query PLAN's order (VectorStoreContract).
        var best = new PriorityQueue<(string Id, double Score), (double Score, string Id)>(WorstFirst.Instance);
        var queryNorm = SquaredNorm(query);
        await using (var reader = await scan.ExecuteReaderAsync(ct).ConfigureAwait(false))
        {
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
            {
                var id = reader.GetString(0);
                var score = JsonCosine(query, queryNorm, reader.GetString(1));
                if (best.Count < k) best.Enqueue((id, score), (score, id));
                else if (best.TryPeek(out _, out var worst) && WorstFirst.Instance.Compare((score, id), worst) > 0)
                    best.EnqueueDequeue((id, score), (score, id));
            }
        }

        var winners = new List<(string Id, double Score)>(best.Count);
        while (best.TryDequeue(out var entry, out _)) winners.Add(entry);
        winners.Sort(static (x, y) => y.Score != x.Score ? y.Score.CompareTo(x.Score) : string.CompareOrdinal(x.Id, y.Id));
        if (winners.Count == 0) return [];

        var payloads = (await conn.QueryAsync<Row>(new CommandDefinition("""
            SELECT vec_id, payload FROM lyntai_vector
            WHERE collection = @collection AND vec_id IN (SELECT value FROM json_each(@ids))
            """, new { collection, ids = ReflectionJson.Serialize(winners.Select(w => w.Id).ToArray()) },
            cancellationToken: ct)).ConfigureAwait(false)).ToDictionary(r => r.VecId, r => r.Payload, StringComparer.Ordinal);
        return [.. winners.Select(w => new VectorMatch(w.Id, payloads.GetValueOrDefault(w.Id, ""), w.Score))];
    }

    private static void AddParameter(DbCommand command, string name, string value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }

    private static double SquaredNorm(float[] vector)
    {
        double sum = 0;
        foreach (var v in vector) sum += (double)v * v;
        return sum;
    }

    /// <summary><see cref="VectorMath.Cosine(float[], float[])"/> against a vector held as its JSON text, summed in
    /// the same order so the two agree bit for bit. A vector of another dimension, an empty or zero one, or text
    /// that is not a list of numbers scores 0 — which is what ranks it last, as the contract says.</summary>
    private static double JsonCosine(float[] query, double queryNorm, string json)
    {
        if (query.Length == 0) return 0;
        var text = json.AsSpan();
        var open = text.IndexOf('[');
        var close = text.LastIndexOf(']');
        if (open < 0 || close < open) return 0;
        text = text[(open + 1)..close];

        double dot = 0, norm = 0;
        var n = 0;
        while (text.Length > 0)
        {
            var comma = text.IndexOf(',');
            var token = comma < 0 ? text : text[..comma];
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return 0;
            if (n == query.Length) return 0;
            dot += (double)query[n] * value;
            norm += (double)value * value;
            n++;
            if (comma < 0) break;
            text = text[(comma + 1)..];
        }

        return n != query.Length || queryNorm == 0 || norm == 0 ? 0 : dot / (Math.Sqrt(queryNorm) * Math.Sqrt(norm));
    }

    private sealed class WorstFirst : IComparer<(double Score, string Id)>
    {
        public static readonly WorstFirst Instance = new();

        public int Compare((double Score, string Id) x, (double Score, string Id) y) =>
            x.Score != y.Score ? x.Score.CompareTo(y.Score) : string.CompareOrdinal(y.Id, x.Id);
    }

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
