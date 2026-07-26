using Dapper;
using Lyntai.Storage;

namespace Lyntai.Storage.Sqlite;

/// <summary>SQLite <see cref="ICuratedMemoryStore"/> over <c>lyntai_curated_memory</c>. Small managed
/// catalog — plain CRUD, no FTS/cap/TTL. Timestamps are TEXT (the shared DateTimeOffset handler);
/// <c>enabled</c> is an INTEGER bool.</summary>
public sealed class SqliteCuratedMemoryStore(IDbConnectionFactory factory, Func<DateTimeOffset>? clock = null) : ICuratedMemoryStore
{
    // the released schema's column is `task`; the record property is TaskKey — alias so Dapper's
    // MatchNamesWithUnderscores maps task_key → TaskKey. WHERE/INSERT keep the bare `task` column name.
    private const string Cols = "id, kind, content, source, enabled, created_at, updated_at, task AS task_key, scope, title";
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<long> AddAsync(string kind, string content, string? source = null, bool enabled = true,
        string? taskKey = null, string? scope = null, bool dedup = false, string? title = null, CancellationToken ct = default)
    {
        var now = _clock();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        if (dedup)
        {
            // idempotent on the (kind, content, taskKey, scope) identity. `IS` is SQLite's null-safe compare,
            // so a null task-key/scope matches a null column (a plain `=` would never match NULL).
            var existing = await conn.ExecuteScalarAsync<long?>(new CommandDefinition("""
                SELECT id FROM lyntai_curated_memory
                WHERE kind = @kind AND content = @content AND task IS @task AND scope IS @scope
                ORDER BY id LIMIT 1
                """, new { kind, content, task = taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
            if (existing is { } id) return id;
        }
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_curated_memory (kind, content, source, enabled, created_at, updated_at, task, scope, title)
            VALUES (@kind, @content, @source, @enabled, @now, @now, @task, @scope, @title)
            RETURNING id
            """, new { kind, content, source, enabled, now, task = taskKey, scope, title }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? source = null,
        string? title = null, CancellationToken ct = default)
    {
        var now = _clock();
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        // COALESCE: only the provided (non-null) fields change; null leaves the column as-is
        var n = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE lyntai_curated_memory
            SET content = COALESCE(@content, content),
                enabled = COALESCE(@enabled, enabled),
                source  = COALESCE(@source, source),
                title   = COALESCE(@title, title),
                updated_at = @now
            WHERE id = @id
            """, new { id, content, enabled, source, title, now }, cancellationToken: ct)).ConfigureAwait(false);
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
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Cols} FROM lyntai_curated_memory WHERE id = @id", new { id }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false,
        string? taskKey = null, string? scope = null, int? limit = null, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<Row>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (@kind IS NULL OR kind = @kind) AND (@task IS NULL OR task = @task)
              AND (@scope IS NULL OR scope = @scope) AND (@enabledOnly = 0 OR enabled = 1)
            ORDER BY kind, created_at, id
            LIMIT @limit
            """, new { kind, task = taskKey, scope, enabledOnly, limit = limit ?? -1 }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToRecord())];
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

    // SQLite stores bool as INTEGER; Dapper won't bind INTEGER→bool through the positional record
    // constructor, so materialize into a settable-property Row (which it does convert) then project.
    private sealed class Row
    {
        public long Id { get; set; }
        public string Kind { get; set; } = "";
        public string Content { get; set; } = "";
        public string? Source { get; set; }
        public bool Enabled { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? TaskKey { get; set; }
        public string? Scope { get; set; }
        public string? Title { get; set; }

        public CuratedMemory ToRecord() => new(Id, Kind, Content, Source, Enabled, CreatedAt, UpdatedAt, TaskKey, Scope, Title);
    }
}
