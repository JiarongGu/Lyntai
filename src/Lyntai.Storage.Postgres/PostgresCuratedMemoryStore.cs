using Dapper;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Postgres;

/// <summary>PostgreSQL <see cref="ICuratedMemoryStore"/> over <c>lyntai_curated_memory</c>. CRUD plus
/// keyword <see cref="SearchAsync"/> (pg_trgm-accelerated ILIKE substring over content/title,
/// recency-ranked, fail-open — the same machinery as <see cref="PostgresMemoryStore"/>); <c>enabled</c>
/// is a native BOOLEAN and timestamps are <c>timestamptz</c>. Nullable update params carry <c>::</c>
/// casts so a NULL "leave unchanged" resolves its type.</summary>
public sealed class PostgresCuratedMemoryStore(IDbConnectionFactory factory,
    ILogger<PostgresCuratedMemoryStore>? logger = null, Func<DateTimeOffset>? clock = null) : ICuratedMemoryStore
{
    private readonly ILogger _logger = logger ?? NullLogger<PostgresCuratedMemoryStore>.Instance;
    // the released schema's column is `task`; the record parameter is TaskKey — alias so Dapper's
    // constructor-name matching maps task_key → TaskKey. WHERE/INSERT keep the bare `task` column name.
    private const string Cols = "id, kind, content, source, enabled, created_at, updated_at, task AS task_key, scope, title";
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<long> AddAsync(string kind, string content, string? source = null, bool enabled = true,
        string? taskKey = null, string? scope = null, bool dedup = false, string? title = null, CancellationToken ct = default)
    {
        var now = _clock();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        if (dedup)
        {
            // idempotent on the (kind, content, taskKey, scope) identity. IS NOT DISTINCT FROM is the null-safe
            // compare, so a null task-key/scope matches a null column (a plain `=` would never match NULL).
            var existing = await conn.ExecuteScalarAsync<long?>(new CommandDefinition("""
                SELECT id FROM lyntai_curated_memory
                WHERE kind = @kind AND content = @content
                  AND task IS NOT DISTINCT FROM @task::text AND scope IS NOT DISTINCT FROM @scope::text
                ORDER BY id LIMIT 1
                """, new { kind, content, task = taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
            if (existing is { } id) return id;
        }
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_curated_memory (kind, content, source, enabled, created_at, updated_at, task, scope, title)
            VALUES (@kind, @content, @source, @enabled, @now, @now, @task, @scope, @title)
            RETURNING id
            """, new { kind, content, enabled, now, source, task = taskKey, scope, title }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? source = null,
        string? title = null, string? kind = null, CancellationToken ct = default)
    {
        var now = _clock();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var n = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE lyntai_curated_memory
            SET content = COALESCE(@content::text, content),
                enabled = COALESCE(@enabled::boolean, enabled),
                source  = COALESCE(@source::text, source),
                title   = COALESCE(@title::text, title),
                kind    = COALESCE(@kind::text, kind),
                updated_at = @now
            WHERE id = @id
            """, new { id, now, content, enabled, source, title, kind }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<bool> RemoveAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_curated_memory WHERE id = @id", new { id }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        return await conn.QuerySingleOrDefaultAsync<CuratedMemory>(new CommandDefinition(
            $"SELECT {Cols} FROM lyntai_curated_memory WHERE id = @id", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // @limit NULL → LIMIT ALL (no cap); enabledOnly is a plain bool predicate; taskKey/scope are strict equality
        var rows = await conn.QueryAsync<CuratedMemory>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (@kind::text IS NULL OR kind = @kind) AND (@task::text IS NULL OR task = @task)
              AND (@scope::text IS NULL OR scope = @scope) AND (NOT @enabledOnly OR enabled)
            -- COLLATE "C" (byte-ordinal) so the text sort matches SQLite's default BINARY collation rather
            -- than the Postgres DB locale collation — identical curated list order across backends.
            ORDER BY kind COLLATE "C", created_at, id
            LIMIT @limit
            """, new { kind, enabledOnly, task = taskKey, scope, limit }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task<IReadOnlyList<CuratedMemory>> SearchAsync(string query, string? kind = null, string? taskKey = null,
        string? scope = null, bool enabledOnly = false, int? limit = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];
        try
        {
            var pattern = LikePattern.Contains(query);
            await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
            // contiguous-substring ILIKE over content OR title (trigram-accelerated by the gin indexes),
            // recency-ranked — same semantics as PostgresMemoryStore.RecallAsync (see the divergence note
            // on ICuratedMemoryStore.SearchAsync). A NULL title never matches (NULL ILIKE → NULL → false).
            var rows = await conn.QueryAsync<CuratedMemory>(new CommandDefinition($"""
                SELECT {Cols} FROM lyntai_curated_memory
                WHERE (content ILIKE @pattern ESCAPE '\' OR title ILIKE @pattern ESCAPE '\')
                  AND (@kind::text IS NULL OR kind = @kind) AND (@task::text IS NULL OR task = @task)
                  AND (@scope::text IS NULL OR scope = @scope) AND (NOT @enabledOnly OR enabled)
                ORDER BY created_at DESC, id DESC
                LIMIT @limit
                """, new { pattern, enabledOnly, kind, task = taskKey, scope, limit }, cancellationToken: ct)).ConfigureAwait(false);
            return [.. rows];
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
        var rows = await conn.QueryAsync<CuratedMemory>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (NOT @enabledOnly OR enabled) AND (task IS NULL OR task = @task){scopeClause}
            ORDER BY kind COLLATE "C", created_at, id
            """, new { task = taskKey, enabledOnly, scopes = scopeArr }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }
}
