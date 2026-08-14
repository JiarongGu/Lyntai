using System.Data.Common;
using System.Text;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Postgres;

/// <summary>PostgreSQL <see cref="ICuratedMemoryStore"/> over <c>lyntai_curated_memory</c>. CRUD plus
/// keyword <see cref="SearchAsync"/> (content-only pg_trgm-accelerated ILIKE substring, recency-ranked,
/// fail-open — the same machinery as <see cref="PostgresMemoryStore"/>); <c>enabled</c> is a native BOOLEAN
/// and timestamps are <c>timestamptz</c>. Arbitrary <c>string→string</c> metadata is one opaque JSON
/// <c>metadata</c> TEXT column (via <see cref="CuratedMetadataJson"/> — no <c>jsonb</c> needed, the query
/// index does the work); each pair is mirrored into the plain relational <c>lyntai_curated_meta</c> index
/// (kept in sync in the write transaction; the FK cascade clears it on delete). Nullable update params
/// carry <c>::</c> casts so a NULL "leave unchanged" resolves its type.</summary>
public sealed class PostgresCuratedMemoryStore(IDbConnectionFactory factory,
    ILogger<PostgresCuratedMemoryStore>? logger = null, Func<DateTimeOffset>? clock = null) : ICuratedMemoryStore
{
    private readonly ILogger _logger = logger ?? NullLogger<PostgresCuratedMemoryStore>.Instance;
    // the released schema's column is `task`; the row property is TaskKey — alias so Dapper's
    // name matching maps task_key → TaskKey. WHERE/INSERT keep the bare `task` column name.
    private const string Cols = "id, kind, content, enabled, created_at, updated_at, task AS task_key, scope, metadata";
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
            // idempotent on the (kind, content, taskKey, scope) identity. IS NOT DISTINCT FROM is the null-safe
            // compare, so a null task-key/scope matches a null column (a plain `=` would never match NULL).
            var existing = await conn.ExecuteScalarAsync<long?>(new CommandDefinition("""
                SELECT id FROM lyntai_curated_memory
                WHERE kind = @kind AND content = @content
                  AND task IS NOT DISTINCT FROM @task::text AND scope IS NOT DISTINCT FROM @scope::text
                ORDER BY id LIMIT 1
                """, new { kind, content, task = taskKey, scope }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (existing is { } dupId) { await tx.CommitAsync(ct).ConfigureAwait(false); return dupId; }
        }
        var id = await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_curated_memory (kind, content, enabled, created_at, updated_at, task, scope, metadata)
            VALUES (@kind, @content, @enabled, @now, @now, @task, @scope, @metaJson::text)
            RETURNING id
            """, new { kind, content, enabled, now, task = taskKey, scope, metaJson }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        await WriteMetaAsync(conn, tx, id, metadata, ct).ConfigureAwait(false);
        await tx.CommitAsync(ct).ConfigureAwait(false);
        return id;
    }

    // taskKey/scope can't ride the SET's COALESCE: NULL is a LEGAL stored value there, so null keeps meaning
    // "leave unchanged" and the empty string is the clear-to-NULL sentinel (interface doc). Resolved in C# so
    // the collision check and the UPDATE write the identical value.
    private static string? Rescope(string? argument, string? current)
        => argument is null ? current : argument.Length == 0 ? null : argument;

    public async Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? kind = null,
        string? taskKey = null, string? scope = null,
        IReadOnlyDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        var now = _clock();
        var replaceMeta = metadata is not null;                    // non-null (incl. empty) = replace; null = leave
        var metaJson = replaceMeta ? CuratedMetadataJson.Serialize(metadata) : null;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);

        // read the current row inside the transaction to resolve the RESULTING dedup identity
        var cur = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Cols} FROM lyntai_curated_memory WHERE id = @id", new { id },
            transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
        if (cur is null) return false;                             // no such row (the tx rolls back on dispose)
        var newKind = kind ?? cur.Kind;
        var newContent = content ?? cur.Content;
        var newTask = Rescope(taskKey, cur.TaskKey);
        var newScope = Rescope(scope, cur.Scope);

        // an identity-mutating update must not land on an identity ANOTHER row already holds — that is precisely
        // the duplicate AddAsync(dedup: true) promises not to create. Refuse, writing nothing. Checked only when
        // the identity actually MOVES, so an enabled/metadata-only edit never refuses and the duplicates
        // dedup:false legitimately allows stay editable. IS NOT DISTINCT FROM is the null-safe compare, as in
        // the dedup add.
        if (newKind != cur.Kind || newContent != cur.Content || newTask != cur.TaskKey || newScope != cur.Scope)
        {
            var clash = await conn.ExecuteScalarAsync<long?>(new CommandDefinition("""
                SELECT id FROM lyntai_curated_memory
                WHERE id <> @id AND kind = @kind AND content = @content
                  AND task IS NOT DISTINCT FROM @task::text AND scope IS NOT DISTINCT FROM @scope::text
                LIMIT 1
                """, new { id, kind = newKind, content = newContent, task = newTask, scope = newScope },
                transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
            if (clash is not null) return false;                   // refused (the tx rolls back on dispose)
        }

        // the identity columns are written from the resolved values; enabled keeps COALESCE (no clear sentinel
        // needed for a bool)
        var n = await conn.ExecuteAsync(new CommandDefinition($$"""
            UPDATE lyntai_curated_memory
            SET content = @newContent::text,
                enabled = COALESCE(@enabled::boolean, enabled),
                kind    = @newKind::text,
                task    = @newTask::text,
                scope   = @newScope::text,
                {{(replaceMeta ? "metadata = @metaJson::text," : "")}}
                updated_at = @now
            WHERE id = @id
            """, new { id, now, newContent, enabled, newKind, newTask, newScope, metaJson }, transaction: tx, cancellationToken: ct)).ConfigureAwait(false);
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
        // lyntai_curated_meta FK is ON DELETE CASCADE — index rows go with the entry
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
        var p = new DynamicParameters(new { kind, enabledOnly, task = taskKey, scope, limit });
        var meta = BuildMetaClause(metadataMatch, "lyntai_curated_memory.id", p);
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // @limit NULL → LIMIT ALL (no cap); enabledOnly is a plain bool predicate; taskKey/scope are strict equality
        var rows = await conn.QueryAsync<Row>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (@kind::text IS NULL OR kind = @kind) AND (@task::text IS NULL OR task = @task)
              AND (@scope::text IS NULL OR scope = @scope) AND (NOT @enabledOnly OR enabled){meta}
            -- COLLATE "C" (byte-ordinal) so the text sort matches SQLite's default BINARY collation rather
            -- than the Postgres DB locale collation — identical curated list order across backends.
            ORDER BY kind COLLATE "C", created_at, id
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

            // Term-wise ILIKE over CONTENT, ranked by how many terms matched and then by recency — same
            // semantics as PostgresMemoryStore.RecallAsync, including its TWO-PASS shape and for the same
            // measured reason: pass 1 stays index-friendly (every pattern >= 3 chars, so the pg_trgm GIN
            // index serves it) and only a MISS widens to the two-character terms of a spaceless script,
            // which the index cannot serve (300k rows: 96.6 ms scan vs 0.90 ms indexed).
            async Task<List<CuratedMemory>> MatchAsync(bool includeShortTerms)
            {
                var kw = SearchTerms.LikeClause(query, "content", "ILIKE",
                    includeShortTerms: includeShortTerms);
                var p = new DynamicParameters(new { enabledOnly, kind, task = taskKey, scope, limit });
                foreach (var (name, value) in kw.Parameters) p.Add(name, value);
                var meta = BuildMetaClause(metadataMatch, "lyntai_curated_memory.id", p);
                var rows = await conn.QueryAsync<Row>(new CommandDefinition($"""
                    SELECT {Cols} FROM lyntai_curated_memory
                    WHERE {kw.Predicate}
                      AND (@kind::text IS NULL OR kind = @kind) AND (@task::text IS NULL OR task = @task)
                      AND (@scope::text IS NULL OR scope = @scope) AND (NOT @enabledOnly OR enabled){meta}
                    ORDER BY {kw.MatchCount} DESC, created_at DESC, id DESC
                    LIMIT @limit
                    """, p, cancellationToken: ct)).ConfigureAwait(false);
                return [.. rows.Select(r => r.ToRecord())];
            }

            var hits = await MatchAsync(includeShortTerms: false).ConfigureAwait(false);
            if (hits.Count == 0 && SearchTerms.HasShortSpacelessTerms(query))
                hits = await MatchAsync(includeShortTerms: true).ConfigureAwait(false);
            return hits;
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
        var scopeArr = scopes as string[] ?? [.. scopes];
        // empty scopes → scope filter disabled; else scope must be null/empty (applies everywhere) or one of
        // the requested scopes (= ANY(...) binds a native array via Npgsql). task-null rows apply to every task.
        var scopeClause = scopeArr.Length == 0 ? "" : " AND (scope IS NULL OR scope = '' OR scope = ANY(@scopes))";
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (NOT @enabledOnly OR enabled) AND (task IS NULL OR task = @task){scopeClause}
            ORDER BY kind COLLATE "C", created_at, id
            """, new { task = taskKey, enabledOnly, scopes = scopeArr }, cancellationToken: ct)).ConfigureAwait(false);
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
