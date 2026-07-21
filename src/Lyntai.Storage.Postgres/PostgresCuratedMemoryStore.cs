using Dapper;
using Lyntai.Storage;

namespace Lyntai.Storage.Postgres;

/// <summary>PostgreSQL <see cref="ICuratedMemoryStore"/> over <c>lyntai_curated_memory</c>. Plain CRUD
/// (no FTS/cap/TTL); <c>enabled</c> is a native BOOLEAN and timestamps are <c>timestamptz</c>. Nullable
/// update params carry <c>::</c> casts so a NULL "leave unchanged" resolves its type.</summary>
public sealed class PostgresCuratedMemoryStore(IDbConnectionFactory factory, Func<DateTimeOffset>? clock = null) : ICuratedMemoryStore
{
    private const string Cols = "id, kind, content, source, enabled, created_at, updated_at, task, scope";
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<long> AddAsync(string kind, string content, string? source = null, bool enabled = true,
        string? task = null, string? scope = null, CancellationToken ct = default)
    {
        var now = _clock();
        using var conn = factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_curated_memory (kind, content, source, enabled, created_at, updated_at, task, scope)
            VALUES (@kind, @content, @source, @enabled, @now, @now, @task, @scope)
            RETURNING id
            """, new
        {
            kind, content, enabled, now,
            source = (object?)source ?? DBNull.Value,
            task = (object?)task ?? DBNull.Value,
            scope = (object?)scope ?? DBNull.Value,
        }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? source = null, CancellationToken ct = default)
    {
        var now = _clock();
        using var conn = factory.Open();
        var n = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE lyntai_curated_memory
            SET content = COALESCE(@content::text, content),
                enabled = COALESCE(@enabled::boolean, enabled),
                source  = COALESCE(@source::text, source),
                updated_at = @now
            WHERE id = @id
            """, new
        {
            id, now,
            content = (object?)content ?? DBNull.Value,
            enabled = (object?)enabled ?? DBNull.Value,
            source = (object?)source ?? DBNull.Value,
        }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<bool> RemoveAsync(long id, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var n = await conn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM lyntai_curated_memory WHERE id = @id", new { id }, cancellationToken: ct)).ConfigureAwait(false);
        return n > 0;
    }

    public async Task<CuratedMemory?> GetAsync(long id, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        return await conn.QuerySingleOrDefaultAsync<CuratedMemory>(new CommandDefinition(
            $"SELECT {Cols} FROM lyntai_curated_memory WHERE id = @id", new { id }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? task = null, int? limit = null, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        // @limit NULL → LIMIT ALL (no cap); enabledOnly is a plain bool predicate; task is strict equality
        var rows = await conn.QueryAsync<CuratedMemory>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (@kind::text IS NULL OR kind = @kind) AND (@task::text IS NULL OR task = @task) AND (NOT @enabledOnly OR enabled)
            -- COLLATE "C" (byte-ordinal) so the text sort matches SQLite's default BINARY collation rather
            -- than the Postgres DB locale collation — identical curated list order across backends.
            ORDER BY kind COLLATE "C", created_at, id
            LIMIT @limit
            """, new
        {
            kind = (object?)kind ?? DBNull.Value, enabledOnly,
            task = (object?)task ?? DBNull.Value,
            limit = (object?)limit ?? DBNull.Value,
        }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }

    public async Task<IReadOnlyList<CuratedMemory>> ForCompositionAsync(string task, IEnumerable<string> scopes,
        bool enabledOnly = true, CancellationToken ct = default)
    {
        var scopeArr = scopes as string[] ?? [.. scopes];
        // empty scopes → scope filter disabled; else scope must be null/empty (applies everywhere) or one of
        // the requested scopes (= ANY(...) binds a native array via Npgsql). task-null rows apply to every task.
        var scopeClause = scopeArr.Length == 0 ? "" : " AND (scope IS NULL OR scope = '' OR scope = ANY(@scopes))";
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<CuratedMemory>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (NOT @enabledOnly OR enabled) AND (task IS NULL OR task = @task){scopeClause}
            ORDER BY kind COLLATE "C", created_at, id
            """, new { task, enabledOnly, scopes = scopeArr }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }
}
