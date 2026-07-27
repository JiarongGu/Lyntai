using System.Data.Common;
using System.Text;
using Dapper;
using Lyntai.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Sqlite;

/// <summary>SQLite <see cref="ICuratedMemoryStore"/> over <c>lyntai_curated_memory</c>. Small managed
/// catalog — CRUD plus keyword <see cref="SearchAsync"/> over the <c>lyntai_curated_fts</c> trigram index
/// (content-only, bm25-ranked, LIKE fallback, fail-open — the same machinery as <see cref="SqliteMemoryStore"/>).
/// Arbitrary <c>string→string</c> metadata is one opaque JSON <c>metadata</c> column (via
/// <see cref="CuratedMetadataJson"/>); it is made QUERYABLE by mirroring each pair into the plain relational
/// <c>lyntai_curated_meta(memory_id, key, value)</c> index (kept in sync in the write transaction; the FK
/// cascade clears it on delete). Timestamps are TEXT (the shared handler); <c>enabled</c> is an INTEGER bool.</summary>
public sealed class SqliteCuratedMemoryStore(IDbConnectionFactory factory,
    ILogger<SqliteCuratedMemoryStore>? logger = null, Func<DateTimeOffset>? clock = null) : ICuratedMemoryStore
{
    // the released schema's column is `task`; the record property is TaskKey — alias so Dapper's
    // MatchNamesWithUnderscores maps task_key → TaskKey. WHERE/INSERT keep the bare `task` column name.
    private const string Cols = "id, kind, content, enabled, created_at, updated_at, task AS task_key, scope, metadata";
    // the same columns `m.`-qualified, for the FTS JOIN in SearchAsync
    private const string ColsM = "m.id, m.kind, m.content, m.enabled, m.created_at, m.updated_at, m.task AS task_key, m.scope, m.metadata";
    private readonly ILogger _logger = logger ?? NullLogger<SqliteCuratedMemoryStore>.Instance;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<long> AddAsync(string kind, string content, bool enabled = true,
        string? taskKey = null, string? scope = null, bool dedup = false,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var now = _clock();
        var metaJson = CuratedMetadataJson.Serialize(metadata);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        if (dedup)
        {
            // idempotent on the (kind, content, taskKey, scope) identity. `IS` is SQLite's null-safe compare,
            // so a null task-key/scope matches a null column (a plain `=` would never match NULL).
            var existing = await conn.ExecuteScalarAsync<long?>(new CommandDefinition("""
                SELECT id FROM lyntai_curated_memory
                WHERE kind = @kind AND content = @content AND task IS @task AND scope IS @scope
                ORDER BY id LIMIT 1
                """, new { kind, content, task = taskKey, scope }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (existing is { } dupId) { await tx.CommitAsync(ct).ConfigureAwait(false); return dupId; }
        }
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_curated_memory (kind, content, enabled, created_at, updated_at, task, scope, metadata)
            VALUES (@kind, @content, @enabled, @now, @now, @task, @scope, @metaJson)
            RETURNING id
            """, new { kind, content, enabled, now, task = taskKey, scope, metaJson }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        await WriteMetaAsync(conn, tx, id, metadata, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return id;
    }

    public async Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var now = _clock();
        var replaceMeta = metadata is not null;                    // non-null (incl. empty) = replace; null = leave
        var metaJson = replaceMeta ? CuratedMetadataJson.Serialize(metadata) : null;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        // COALESCE: only the provided (non-null) fields change; kind re-categorises in place. The metadata
        // column is set only when a replacement is requested (@metaJson may be null → clears it).
        var n = await conn.ExecuteAsync(new CommandDefinition($$"""
            UPDATE lyntai_curated_memory
            SET content = COALESCE(@content, content),
                enabled = COALESCE(@enabled, enabled),
                kind    = COALESCE(@kind, kind),
                {{(replaceMeta ? "metadata = @metaJson," : "")}}
                updated_at = @now
            WHERE id = @id
            """, new { id, content, enabled, kind, metaJson, now }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (replaceMeta && n > 0)
        {
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM lyntai_curated_meta WHERE memory_id = @id", new { id }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            await WriteMetaAsync(conn, tx, id, metadata, ct).ConfigureAwait(false);
        }
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<bool> RemoveAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // the lyntai_curated_meta FK is ON DELETE CASCADE (foreign_keys=ON per connection) — index rows go too
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_curated_memory WHERE id = @id", new { id }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Cols} FROM lyntai_curated_memory WHERE id = @id", new { id }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        var p = new DynamicParameters(new { kind, task = taskKey, scope, enabledOnly, limit = limit ?? -1 });
        var meta = BuildMetaClause(metadataMatch, "lyntai_curated_memory.id", p);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (@kind IS NULL OR kind = @kind) AND (@task IS NULL OR task = @task)
              AND (@scope IS NULL OR scope = @scope) AND (@enabledOnly = 0 OR enabled = 1){meta}
            ORDER BY kind, created_at, id
            LIMIT @limit
            """, p, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToRecord())];
    }

    public async Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null, string? taskKey = null,
        string? scope = null, bool enabledOnly = false, int? limit = null,
        IReadOnlyDictionary<string, string>? metadataMatch = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        try
        {
            await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);

            var match = FtsQuery.Build(query);
            if (match is not null)
            {
                try
                {
                    var pf = new DynamicParameters(new { match, kind, task = taskKey, scope, enabledOnly, limit = limit ?? -1 });
                    var mf = BuildMetaClause(metadataMatch, "m.id", pf);
                    var hits = (await conn.QueryAsync<Row>(new CommandDefinition($"""
                        SELECT {ColsM}
                        FROM lyntai_curated_fts JOIN lyntai_curated_memory m ON m.id = lyntai_curated_fts.rowid
                        WHERE lyntai_curated_fts MATCH @match
                          AND (@kind IS NULL OR m.kind = @kind) AND (@task IS NULL OR m.task = @task)
                          AND (@scope IS NULL OR m.scope = @scope) AND (@enabledOnly = 0 OR m.enabled = 1){mf}
                        ORDER BY bm25(lyntai_curated_fts), m.id DESC LIMIT @limit -- id tiebreak: equal-score rows page deterministically
                        """, pf, cancellationToken: ct)).ConfigureAwait(false)).AsList();
                    if (hits.Count > 0) return [.. hits.Select(r => r.ToRecord())];
                    // no trigram hit → fall through to LIKE (covers punctuation-heavy / <3-char queries)
                }
                catch (SqliteException ex)
                {
                    _logger.LogWarning(ex, "curated FTS search failed; falling back to LIKE");
                }
            }

            var pl = new DynamicParameters(new { pattern = LikePattern.Contains(query), kind, task = taskKey, scope, enabledOnly, limit = limit ?? -1 });
            var ml = BuildMetaClause(metadataMatch, "lyntai_curated_memory.id", pl);
            var likeHits = await conn.QueryAsync<Row>(new CommandDefinition($"""
                SELECT {Cols} FROM lyntai_curated_memory
                WHERE content LIKE @pattern ESCAPE '\'
                  AND (@kind IS NULL OR kind = @kind) AND (@task IS NULL OR task = @task)
                  AND (@scope IS NULL OR scope = @scope) AND (@enabledOnly = 0 OR enabled = 1){ml}
                ORDER BY created_at DESC, id DESC LIMIT @limit
                """, pl, cancellationToken: ct)).ConfigureAwait(false);
            return [.. likeHits.Select(r => r.ToRecord())];
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "curated search failed; returning empty (fail-open)");
            return [];
        }
    }

    public async Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string taskKey, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default)
    {
        var scopeList = scopes as IReadOnlyList<string> ?? [.. scopes];
        // empty scopes → scope filter disabled; else the entry's scope must be null/empty (applies everywhere)
        // or one of the requested scopes. task-key-null rows apply to every task.
        var scopeClause = scopeList.Count == 0 ? "" : " AND (scope IS NULL OR scope = '' OR scope IN @scopes)";
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (@enabledOnly = 0 OR enabled = 1) AND (task IS NULL OR task = @task){scopeClause}
            ORDER BY kind, created_at, id
            """, new { task = taskKey, enabledOnly, scopes = scopeList }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToRecord())];
    }

    // mirror each metadata pair into the relational index (the queryable side of the opaque JSON column)
    private static async Task WriteMetaAsync(DbConnection conn, DbTransaction tx, long id,
        IReadOnlyDictionary<string, string>? metadata, CancellationToken ct)
    {
        if (metadata is null || metadata.Count == 0) return;
        foreach (var kv in metadata)
            await conn.ExecuteAsync(new CommandDefinition(
                "INSERT INTO lyntai_curated_meta(memory_id, key, value) VALUES (@id, @k, @v)",
                new { id, k = kv.Key, v = kv.Value }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
    }

    // metadataMatch → an AND of EXISTS against the relational index (params bound; no interpolated user text)
    private static string BuildMetaClause(IReadOnlyDictionary<string, string>? match, string idExpr, DynamicParameters p)
    {
        if (match is null || match.Count == 0) return "";
        var sb = new StringBuilder();
        var i = 0;
        foreach (var kv in match)
        {
            p.Add("mk" + i, kv.Key);
            p.Add("mv" + i, kv.Value);
            sb.Append(" AND EXISTS (SELECT 1 FROM lyntai_curated_meta mm").Append(i)
              .Append(" WHERE mm").Append(i).Append(".memory_id = ").Append(idExpr)
              .Append(" AND mm").Append(i).Append(".key = @mk").Append(i)
              .Append(" AND mm").Append(i).Append(".value = @mv").Append(i).Append(')');
            i++;
        }
        return sb.ToString();
    }

    // SQLite stores bool as INTEGER; Dapper won't bind INTEGER→bool through the positional record
    // constructor, so materialize into a settable-property Row (which it does convert) then project.
    private sealed class Row
    {
        public long Id { get; set; }
        public string Kind { get; set; } = "";
        public string Content { get; set; } = "";
        public bool Enabled { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? TaskKey { get; set; }
        public string? Scope { get; set; }
        public string? Metadata { get; set; }

        public CuratedMemory ToRecord() => new(Id, Kind, Content, Enabled, CreatedAt, UpdatedAt,
            TaskKey, Scope, CuratedMetadataJson.Deserialize(Metadata));
    }
}
