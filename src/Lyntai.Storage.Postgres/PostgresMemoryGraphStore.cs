using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Lyntai.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Postgres;

/// <summary>PostgreSQL <see cref="IMemoryGraphStore"/> over <c>lyntai_memory_node</c> +
/// <c>lyntai_memory_edge</c>, with substring recall through the <c>pg_trgm</c> GIN index.
/// <para>The parallel with <c>SqliteMemoryGraphStore</c> is deliberate and is NOT duplication waiting to be
/// extracted: the two differ by dialect necessity — <c>GREATEST</c> versus <c>MAX</c>, an <c>ILIKE</c> over
/// a GIN index versus an FTS5 virtual table and its three triggers, and the table reference Postgres
/// requires in <c>DO UPDATE SET</c>. The shared store contract is what holds them to one behaviour.</para>
/// <para><b>Age is a subtraction, not a duration</b> — <c>lyntai_memory_position</c> holds a monotone
/// position per engine — and <b>the decay curve is never evaluated here</b>. Recall matches the query as a
/// CONTIGUOUS substring and ranks by recency; SQLite matches any trigram token and ranks by bm25. That
/// divergence is by design and is the same one <see cref="IMemoryStore.RecallAsync"/> already documents —
/// the portable guarantee is single-token.</para></summary>
/// <param name="factory">Connection factory.</param>
/// <param name="logger">Optional.</param>
/// <param name="clock">Time source for the audit timestamps; null takes the system clock.</param>
public sealed class PostgresMemoryGraphStore(
    IDbConnectionFactory factory,
    ILogger<PostgresMemoryGraphStore>? logger = null,
    Func<DateTimeOffset>? clock = null) : IMemoryGraphStore
{
    private readonly ILogger _logger = logger ?? NullLogger<PostgresMemoryGraphStore>.Instance;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    // Degree/Strength are plain aggregates and both ages are plain subtractions against @position — the
    // store applies no decay; the policy does. No CAST: DOUBLE PRECISION binds to double directly.
    private const string NodeColumns = """
        n.id AS "Id", n.engine AS "Engine", n.task_key AS "TaskKey", n.scope AS "Scope",
        n.headline AS "Headline", n.content AS "Content", n.grade AS "Grade",
        n.created_at AS "CreatedAt", n.recall_count AS "RecallCount",
        n.stability AS "Stability", n.metadata AS "Metadata",
        (@position - n.last_recalled_position) AS "Age",
        (SELECT COUNT(*) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS "Degree",
        (SELECT COALESCE(SUM(e.weight), 0) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS "Strength",
        (SELECT COALESCE(@position - MAX(e.strengthened_position), 0)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS "StrengthAge"
        """;

    // GREATEST guards a zero stability: dividing by zero raises here rather than yielding NULL as it does
    // on SQLite, but the guard is what keeps both backends agreeing instead of one erroring.
    private const string AgeOverStability =
        "(@position - n.last_recalled_position) / GREATEST(n.stability, 0.000001)";

    private const string CutoffPredicate =
        "(n.grade = @authoritative OR @cut IS NULL OR " + AgeOverStability + " <= @cut)";

    // Bound as a parameter, never a literal: MemoryGrade is Inherit=0, Associative=1, Authoritative=2, so
    // a hand-written "grade = 1" silently means the OPPOSITE of what it reads like.
    private static readonly int Authoritative = (int)MemoryGrade.Authoritative;

    /// <inheritdoc />
    public async Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var hash = ContentHash(write.Content);
        var metadata = CuratedMetadataJson.Serialize(write.Metadata);
        using var conn = factory.Open();
        using var tx = conn.BeginTransaction();

        // advance the engine's position FIRST and atomically, so the new entry's own age is zero relative
        // to the position it is stamped with
        var position = await conn.ExecuteScalarAsync<double>(new CommandDefinition("""
            INSERT INTO lyntai_memory_position (engine, position) VALUES (@engine, @advance)
            ON CONFLICT (engine) DO UPDATE SET position = lyntai_memory_position.position + @advance
            RETURNING position
            """, new { engine = write.Engine, advance = Math.Max(0, write.Advance) },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_memory_node
                (engine, task_key, scope, headline, content, content_hash, grade, metadata,
                 created_at, last_recalled_position, recall_count, stability)
            VALUES (@engine, @taskKey, @scope, @headline, @content, @hash, @grade, @metadata,
                    @now, @position, 0, @stability)
            ON CONFLICT (engine, task_key, scope, content_hash)
                DO UPDATE SET last_recalled_position = @position, grade = @grade, headline = @headline
            RETURNING id
            """, new
        {
            engine = write.Engine, taskKey = write.TaskKey, scope = write.Scope,
            headline = write.Headline, content = write.Content, hash, grade = (int)write.Grade,
            metadata, now = _clock(), position, stability = write.InitialStability,
        }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        tx.Commit();
        return id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, double? maxAgeOverStability, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var cut = maxAgeOverStability;
        using var conn = factory.Open();
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = LikePattern.Contains(query);
            return await QueryAsync(conn, $"""
                SELECT {NodeColumns}
                FROM lyntai_memory_node n
                WHERE n.engine = @engine AND n.task_key = @taskKey
                  AND (@scope IS NULL OR n.scope = @scope)
                  AND (n.grade = @authoritative OR n.content ILIKE @pattern)
                  AND {CutoffPredicate}
                ORDER BY n.last_recalled_position DESC, n.id DESC
                LIMIT @limit
                """, new { engine, taskKey, scope, pattern, cut, position, limit, authoritative = Authoritative },
                ct).ConfigureAwait(false);
        }

        return await QueryAsync(conn, $"""
            SELECT {NodeColumns}
            FROM lyntai_memory_node n
            WHERE n.engine = @engine AND n.task_key = @taskKey
              AND (@scope IS NULL OR n.scope = @scope)
              AND {CutoffPredicate}
            ORDER BY n.last_recalled_position DESC, n.id DESC
            LIMIT @limit
            """, new { engine, taskKey, scope, cut, position, limit, authoritative = Authoritative },
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine,
        IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        if (ids.Count == 0) return [];

        using var conn = factory.Open();
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);
        var idArray = ids.ToArray();
        // = ANY / <> ALL over a bound array rather than Dapper's IN expansion: Npgsql binds an array
        // natively, so the statement text stays constant and stays plan-cacheable
        var rows = await conn.QueryAsync<NodeRow, double, double, GraphNeighbour>(
            new CommandDefinition($"""
                SELECT {NodeColumns}, x.w AS "EdgeWeight", (@position - x.at) AS "EdgeAge"
                FROM (SELECT e.to_id AS id, MAX(e.weight) AS w, MAX(e.strengthened_position) AS at
                      FROM lyntai_memory_edge e
                      WHERE e.from_id = ANY(@idArray) AND e.to_id <> ALL(@idArray)
                      GROUP BY e.to_id) x
                JOIN lyntai_memory_node n ON n.id = x.id
                WHERE n.engine = @engine
                ORDER BY x.w DESC, n.id DESC
                LIMIT @limit
                """, new { idArray, engine, limit, position }, cancellationToken: ct),
            (row, weight, age) => new GraphNeighbour(ToNode(row), weight, age),
            splitOn: "EdgeWeight,EdgeAge").ConfigureAwait(false);
        return [.. rows];
    }

    /// <inheritdoc />
    public async Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = factory.Open();
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);
        var hits = await QueryAsync(conn,
            $"SELECT {NodeColumns} FROM lyntai_memory_node n WHERE n.id = @id AND n.engine = @engine",
            new { id, engine, position }, ct).ConfigureAwait(false);
        return hits.Count == 0 ? null : hits[0];
    }

    /// <inheritdoc />
    public async Task TouchAsync(string engine, IReadOnlyCollection<GraphTouch> touches,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(touches);
        ct.ThrowIfCancellationRequested();
        if (touches.Count == 0) return;

        using var conn = factory.Open();
        // a recall does NOT advance the position — it stamps wherever the engine already is
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE lyntai_memory_node
            SET last_recalled_position = @position, stability = @Stability,
                recall_count = lyntai_memory_node.recall_count + 1
            WHERE id = @Id AND engine = @engine
            """, touches.Select(t => new { t.Id, t.Stability, engine, position }),
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task LinkAsync(string engine, long from, long to, string? kind, double weight,
        bool symmetric, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (from == to) return; // a self-edge is never useful and would skew Degree

        using var conn = factory.Open();
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);
        await StrengthenAsync(conn, from, to, kind ?? "", weight, position, ct).ConfigureAwait(false);
        if (symmetric)
            await StrengthenAsync(conn, to, from, kind ?? "", weight, position, ct).ConfigureAwait(false);
    }

    // Postgres requires the table reference in the DO UPDATE SET expression, where SQLite takes a bare
    // column — one of the dialect differences that make sharing this text impossible.
    private static Task StrengthenAsync(IDbConnection conn, long from, long to, string kind, double weight,
        double position, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_edge (from_id, to_id, kind, weight, strengthened_position)
            VALUES (@from, @to, @kind, @weight, @position)
            ON CONFLICT (from_id, to_id, kind)
                DO UPDATE SET weight = lyntai_memory_edge.weight + @weight,
                              strengthened_position = @position
            """, new { from, to, kind, weight, position }, cancellationToken: ct));

    /// <inheritdoc />
    public async Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var cut = maxAgeOverStability;
        var createdBefore = olderThan is null ? (DateTimeOffset?)null : _clock() - olderThan.Value;
        using var conn = factory.Open();
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);
        return await conn.ExecuteAsync(new CommandDefinition($"""
            DELETE FROM lyntai_memory_node WHERE id IN (
                SELECT n.id FROM lyntai_memory_node n
                WHERE n.engine = @engine AND n.task_key = @taskKey
                  AND (@scope IS NULL OR n.scope = @scope)
                  AND n.grade <> @authoritative
                  AND ( (@cut IS NOT NULL AND {AgeOverStability} > @cut)
                        OR (@createdBefore IS NOT NULL AND n.created_at < @createdBefore) ))
            """, new { engine, taskKey, scope, cut, createdBefore, position, authoritative = Authoritative },
            cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ForgetAsync(string engine, string taskKey, string? scope,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_node
            WHERE engine = @engine AND task_key = @taskKey AND (@scope IS NULL OR scope = @scope)
            """, new { engine, taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Where the engine's position currently stands. Zero for an engine nothing has been written
    /// to — which is correct: nothing has happened in it, so nothing has aged.</summary>
    private static async Task<double> PositionAsync(IDbConnection conn, string engine, CancellationToken ct) =>
        await conn.ExecuteScalarAsync<double?>(new CommandDefinition(
            "SELECT position FROM lyntai_memory_position WHERE engine = @engine",
            new { engine }, cancellationToken: ct)).ConfigureAwait(false) ?? 0;

    private static async Task<IReadOnlyList<GraphNode>> QueryAsync(IDbConnection conn, string sql,
        object parameters, CancellationToken ct)
    {
        var rows = (await conn.QueryAsync<NodeRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        return [.. rows.Select((row, i) => ToNode(row) with
        {
            Relevance = rows.Count <= 1 ? 1 : 1 - (double)i / rows.Count,
        })];
    }

    private static GraphNode ToNode(NodeRow row) => new(
        row.Id, row.Engine, row.TaskKey, row.Scope, row.Headline, row.Content, (MemoryGrade)row.Grade,
        row.CreatedAt, row.RecallCount, row.Stability, row.Age, 1, row.Degree,
        CuratedMetadataJson.Deserialize(row.Metadata), row.Strength, row.StrengthAge);

    private static string ContentHash(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

    /// <summary>Materialization type — settable properties rather than a positional record, which
    /// sidesteps Dapper's record-constructor exact-type matching (the reason every Postgres store here uses
    /// one).</summary>
    private sealed class NodeRow
    {
        public long Id { get; set; }
        public string Engine { get; set; } = "";
        public string TaskKey { get; set; } = "";
        public string Scope { get; set; } = "";
        public string Headline { get; set; } = "";
        public string Content { get; set; } = "";
        public int Grade { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public int RecallCount { get; set; }
        public double Stability { get; set; }
        public double Age { get; set; }
        public string? Metadata { get; set; }
        public int Degree { get; set; }
        public double Strength { get; set; }
        public double StrengthAge { get; set; }
    }
}
