using System.Data;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Lyntai.Memory;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Sqlite;

/// <summary>SQLite <see cref="IMemoryGraphStore"/> over <c>lyntai_memory_node</c> +
/// <c>lyntai_memory_edge</c>, with recall through the <c>lyntai_memory_node_fts</c> trigram index
/// (bm25-ranked, LIKE fallback, most-recent last) — the same machinery as
/// <see cref="SqliteMemoryStore"/>.
/// <para><b>Age is a subtraction, not a duration.</b> <c>lyntai_memory_position</c> holds a monotone
/// position per engine, advanced by each write; an entry's age is how far it has moved since that entry
/// was last used. So no date arithmetic appears in any query — which also removes the hazard the previous
/// design sat next to, where <c>julianday</c> returning NULL on an unparseable timestamp would silently
/// exclude every row.</para>
/// <para><b>The decay curve is never evaluated here.</b> Candidates are bounded by a plain
/// <c>age / stability &lt;= @cut</c> comparison whose right-hand side comes from
/// <see cref="IRetrievabilityPolicy.CandidateCutoff"/>; exact retrievability and ranking happen in the
/// engine, because no fixed SQL could encode a policy the application supplies.</para>
/// <para><b><see cref="GraphNode.Relevance"/> is this backend's own rank position, normalized</b> to the
/// contractual 0..1. <c>bm25()</c> returns an unbounded negative score, so rather than invent a
/// normalization the store reports a monotone transform of its own ordering — which is all the engine's
/// rank multiplication needs.</para></summary>
/// <param name="factory">Connection factory — the ONLY way to open a connection here, because it applies
/// <c>foreign_keys=ON</c> per connection and the edge cascade depends on it.</param>
/// <param name="logger">Optional.</param>
/// <param name="clock">Time source for the audit timestamps; null takes the system clock.</param>
public sealed class SqliteMemoryGraphStore(
    IDbConnectionFactory factory,
    ILogger<SqliteMemoryGraphStore>? logger = null,
    Func<DateTimeOffset>? clock = null) : IMemoryGraphStore
{
    private readonly ILogger _logger = logger ?? NullLogger<SqliteMemoryGraphStore>.Instance;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    // Alias every column explicitly: a name mismatch is a SILENT null, not an error. Degree/Strength are
    // plain aggregates, and both ages are plain subtractions against @position — the store applies no
    // decay; the policy does.
    private const string NodeColumns = """
        n.id AS Id, n.engine AS Engine, n.task_key AS TaskKey, n.scope AS Scope,
        n.headline AS Headline, n.content AS Content, n.grade AS Grade,
        n.created_at AS CreatedAt, n.recall_count AS RecallCount,
        CAST(n.stability AS REAL) AS Stability, n.metadata AS Metadata,
        CAST(@position - n.last_recalled_position AS REAL) AS Age,
        (SELECT COUNT(*) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS Degree,
        (SELECT CAST(COALESCE(SUM(e.weight), 0) AS REAL) FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS Strength,
        (SELECT CAST(COALESCE(@position - MAX(e.strengthened_position), 0) AS REAL)
         FROM lyntai_memory_edge e WHERE e.from_id = n.id) AS StrengthAge
        """;

    // The MAX() guard is load-bearing: a zero stability divides by zero, SQLite evaluates that to NULL, and
    // a NULL predicate excludes the row SILENTLY — losing a memory rather than erroring.
    private const string AgeOverStability =
        "(@position - n.last_recalled_position) / MAX(CAST(n.stability AS REAL), 0.000001)";

    // Seeding applies NO faintness bound — decay buries by rank in the engine, never by excluding a row
    // here. This predicate is PruneAsync's alone, where removing a memory is the explicit intent.

    // Bound as a PARAMETER rather than written as a literal: MemoryGrade is Inherit=0, Associative=1,
    // Authoritative=2, so a hand-written "grade = 1" silently means the OPPOSITE of what it reads like.
    private static readonly int Authoritative = (int)MemoryGrade.Authoritative;

    /// <inheritdoc />
    public async Task<long> UpsertAsync(GraphNodeWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        var hash = ContentHash(write.Content);
        var metadata = CuratedMetadataJson.Serialize(write.Metadata);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        // advance the engine's position FIRST and atomically, so the new entry's own age is zero relative
        // to the position it is stamped with
        var position = await conn.ExecuteScalarAsync<double>(new CommandDefinition("""
            INSERT INTO lyntai_memory_position (engine, position) VALUES (@engine, @advance)
            ON CONFLICT(engine) DO UPDATE SET position = position + @advance
            RETURNING position
            """, new { engine = write.Engine, advance = Math.Max(0, write.Advance) },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        // atomic on ux_lyntai_memory_node_dedup: identical content REFRESHES rather than duplicating.
        // RETURNING id, never last_insert_rowid() — that is per-connection and returns 0 on another
        // pooled connection.
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_memory_node
                (engine, task_key, scope, headline, content, content_hash, grade, metadata,
                 created_at, last_recalled_position, recall_count, stability)
            VALUES (@engine, @taskKey, @scope, @headline, @content, @hash, @grade, @metadata,
                    @now, @position, 0, @stability)
            ON CONFLICT(engine, task_key, scope, content_hash)
                DO UPDATE SET last_recalled_position = @position, grade = @grade, headline = @headline
            RETURNING id
            """, new
        {
            engine = write.Engine, taskKey = write.TaskKey, scope = write.Scope,
            headline = write.Headline, content = write.Content, hash, grade = (int)write.Grade,
            metadata, now = _clock(), position, stability = write.InitialStability,
        }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNode>> SeedAsync(string engine, string taskKey, string? scope,
        string? query, int limit, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);

        var match = FtsQuery.Build(query);
        if (match is not null)
        {
            try
            {
                var hits = await QueryAsync(conn, $"""
                    SELECT {NodeColumns}
                    FROM lyntai_memory_node_fts f JOIN lyntai_memory_node n ON n.id = f.rowid
                    WHERE f.lyntai_memory_node_fts MATCH @match
                      AND n.engine = @engine AND n.task_key = @taskKey
                      AND (@scope IS NULL OR n.scope = @scope)
                    ORDER BY bm25(f.lyntai_memory_node_fts), n.id DESC
                    LIMIT @limit
                    """, new { match, engine, taskKey, scope, position, limit, authoritative = Authoritative },
                    ct).ConfigureAwait(false);
                if (hits.Count > 0) return hits;
                // no trigram hit → fall through to LIKE (covers punctuation-heavy and short queries)
            }
            catch (SqliteException ex)
            {
                _logger.LogWarning(ex, "graph FTS seed failed for {Engine}/{Task}; falling back to LIKE",
                    engine, taskKey);
            }
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            var pattern = LikePattern.Contains(query);
            return await QueryAsync(conn, $"""
                SELECT {NodeColumns}
                FROM lyntai_memory_node n
                WHERE n.engine = @engine AND n.task_key = @taskKey
                  AND (@scope IS NULL OR n.scope = @scope)
                  AND (n.grade = @authoritative OR n.content LIKE @pattern ESCAPE '\')
                ORDER BY n.last_recalled_position DESC, n.id DESC
                LIMIT @limit
                """, new { engine, taskKey, scope, pattern, position, limit, authoritative = Authoritative },
                ct).ConfigureAwait(false);
        }

        return await QueryAsync(conn, $"""
            SELECT {NodeColumns}
            FROM lyntai_memory_node n
            WHERE n.engine = @engine AND n.task_key = @taskKey
              AND (@scope IS NULL OR n.scope = @scope)
            ORDER BY n.last_recalled_position DESC, n.id DESC
            LIMIT @limit
            """, new { engine, taskKey, scope, position, limit, authoritative = Authoritative },
            ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GraphNeighbour>> NeighboursAsync(string engine,
        IReadOnlyCollection<long> ids, int limit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        ct.ThrowIfCancellationRequested();
        if (ids.Count == 0) return [];

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);
        var idList = ids.ToList();
        // RAW weight and staleness — the engine applies the decay curve and re-ranks; ordering here is a
        // cheap pre-sort only
        var rows = await conn.QueryAsync<NodeRow, double, double, GraphNeighbour>(
            new CommandDefinition($"""
                SELECT {NodeColumns},
                       CAST(x.w AS REAL) AS EdgeWeight,
                       CAST(@position - x.at AS REAL) AS EdgeAge
                FROM (SELECT e.to_id AS id, MAX(e.weight) AS w, MAX(e.strengthened_position) AS at
                      FROM lyntai_memory_edge e
                      WHERE e.from_id IN @idList AND e.to_id NOT IN @idList
                      GROUP BY e.to_id) x
                JOIN lyntai_memory_node n ON n.id = x.id
                WHERE n.engine = @engine
                ORDER BY x.w DESC, n.id DESC
                LIMIT @limit
                """, new { idList, engine, limit, position }, cancellationToken: ct),
            (row, weight, age) => new GraphNeighbour(ToNode(row), weight, age),
            splitOn: "EdgeWeight,EdgeAge").ConfigureAwait(false);
        return [.. rows];
    }

    /// <inheritdoc />
    public async Task<GraphNode?> GetAsync(string engine, long id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
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

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // a recall does NOT advance the position — it stamps wherever the engine already is
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE lyntai_memory_node
            SET last_recalled_position = @position, stability = @Stability,
                recall_count = recall_count + 1
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

        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);
        await StrengthenAsync(conn, from, to, kind ?? "", weight, position, ct).ConfigureAwait(false);
        if (symmetric)
            await StrengthenAsync(conn, to, from, kind ?? "", weight, position, ct).ConfigureAwait(false);
    }

    private static Task StrengthenAsync(IDbConnection conn, long from, long to, string kind, double weight,
        double position, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_edge (from_id, to_id, kind, weight, strengthened_position)
            VALUES (@from, @to, @kind, @weight, @position)
            ON CONFLICT(from_id, to_id, kind)
                DO UPDATE SET weight = weight + @weight, strengthened_position = @position
            """, new { from, to, kind, weight, position }, cancellationToken: ct));

    /// <inheritdoc />
    public async Task<int> PruneAsync(string engine, string taskKey, string? scope,
        double? maxAgeOverStability, TimeSpan? olderThan, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var cut = maxAgeOverStability;
        var createdBefore = olderThan is null ? (DateTimeOffset?)null : _clock() - olderThan.Value;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var position = await PositionAsync(conn, engine, ct).ConfigureAwait(false);
        // edges go with the nodes through ON DELETE CASCADE — which only fires because the factory sets
        // foreign_keys=ON per connection
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
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_node
            WHERE engine = @engine AND task_key = @taskKey AND (@scope IS NULL OR scope = @scope)
            """, new { engine, taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
    }

    /// <summary>Where the engine's position currently stands. Zero for an engine nothing has been written
    /// to — which is correct: nothing has happened in it, so nothing has aged.</summary>
    private static async Task<double> PositionAsync(IDbConnection conn, string engine, CancellationToken ct) =>
        await conn.ExecuteScalarAsync<double?>(new CommandDefinition(
            "SELECT CAST(position AS REAL) FROM lyntai_memory_position WHERE engine = @engine",
            new { engine }, cancellationToken: ct)).ConfigureAwait(false) ?? 0;

    private static async Task<IReadOnlyList<GraphNode>> QueryAsync(IDbConnection conn, string sql,
        object parameters, CancellationToken ct)
    {
        var rows = (await conn.QueryAsync<NodeRow>(
            new CommandDefinition(sql, parameters, cancellationToken: ct)).ConfigureAwait(false)).AsList();
        // Relevance is this backend's own rank position, normalized — see the type docs
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

    /// <summary>Materialization type — settable properties, never a positional record: Dapper will not bind
    /// a SQLite INTEGER to a positional record constructor parameter, and a property-mapped row sidesteps
    /// its exact-type matching entirely.</summary>
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
