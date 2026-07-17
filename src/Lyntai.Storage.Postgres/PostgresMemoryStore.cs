using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Postgres;

/// <summary>
/// Task-scoped memory over <c>lyntai_memory_entry</c> + a <c>pg_trgm</c> GIN index. Same contract as
/// the SQLite backend: dedup on remember, per-entry TTL, per-(task, scope) cap, and fail-open recall.
/// Recall matches by ILIKE SUBSTRING (trigram-accelerated, works for CJK substrings) ordered by
/// recency — the Postgres analogue of the SQLite FTS5-trigram + LIKE path.
/// Null parameters used in IS-NULL checks are cast (Npgsql can't infer the type otherwise).
/// </summary>
public sealed class PostgresMemoryStore(
    IDbConnectionFactory factory,
    LyntaiOptions options,
    ILogger<PostgresMemoryStore>? logger = null,
    Func<DateTimeOffset>? clock = null) : IMemoryStore
{
    private const string SelectColumns =
        "id AS Id, task_key AS TaskKey, scope AS Scope, content AS Content, created_at AS CreatedAt";

    private readonly ILogger _logger = logger ?? NullLogger<PostgresMemoryStore>.Instance;
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var now = _clock();
        var expiresAt = ttl is null ? (DateTimeOffset?)null : now + ttl.Value;
        using var conn = factory.Open();

        var refreshed = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE lyntai_memory_entry SET created_at = @now, expires_at = @expiresAt
            WHERE task_key = @taskKey AND scope = @scope AND content = @content
            """, new { taskKey, scope, content, now, expiresAt = (object?)expiresAt ?? DBNull.Value }, cancellationToken: ct)).ConfigureAwait(false);

        if (refreshed == 0)
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                INSERT INTO lyntai_memory_entry (task_key, scope, content, created_at, expires_at)
                VALUES (@taskKey, @scope, @content, @now, @expiresAt)
                """, new { taskKey, scope, content, now, expiresAt = (object?)expiresAt ?? DBNull.Value }, cancellationToken: ct)).ConfigureAwait(false);
        }

        // bounded: trim the oldest beyond the per-(task, scope) cap
        await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_entry
            WHERE task_key = @taskKey AND scope = @scope AND id NOT IN (
                SELECT id FROM lyntai_memory_entry WHERE task_key = @taskKey AND scope = @scope
                ORDER BY id DESC LIMIT @cap)
            """, new { taskKey, scope, cap = options.MemoryCapPerScope }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        var take = limit ?? options.MemoryRecallLimit;
        var now = _clock();
        try
        {
            using var conn = factory.Open();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var pattern = "%" + query.Trim()
                    .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                var hits = await conn.QueryAsync<Row>(new CommandDefinition($"""
                    SELECT {SelectColumns} FROM lyntai_memory_entry
                    WHERE task_key = @taskKey AND (@scope::text IS NULL OR scope = @scope)
                      AND (expires_at IS NULL OR expires_at > @now)
                      AND content ILIKE @pattern ESCAPE '\'
                    ORDER BY id DESC LIMIT @take
                    """, new { taskKey, scope, pattern, take, now }, cancellationToken: ct)).ConfigureAwait(false);
                return [.. hits.Select(r => r.ToEntity())];
            }

            var recent = await conn.QueryAsync<Row>(new CommandDefinition($"""
                SELECT {SelectColumns} FROM lyntai_memory_entry
                WHERE task_key = @taskKey AND (@scope::text IS NULL OR scope = @scope)
                  AND (expires_at IS NULL OR expires_at > @now)
                ORDER BY id DESC LIMIT @take
                """, new { taskKey, scope, take, now }, cancellationToken: ct)).ConfigureAwait(false);
            return [.. recent.Select(r => r.ToEntity())];
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "memory recall failed for {Task}; returning empty (fail-open)", taskKey);
            return [];
        }
    }

    public async Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_entry WHERE task_key = @taskKey AND (@scope::text IS NULL OR scope = @scope)
            """, new { taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        var now = _clock();
        var cutoff = olderThan is null ? (DateTimeOffset?)null : now - olderThan.Value;
        using var conn = factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_entry
            WHERE (@taskKey::text IS NULL OR task_key = @taskKey)
              AND ( (expires_at IS NOT NULL AND expires_at <= @now)
                    OR (@cutoff::timestamptz IS NOT NULL AND created_at < @cutoff) )
            """, new { taskKey, now, cutoff = (object?)cutoff ?? DBNull.Value }, cancellationToken: ct)).ConfigureAwait(false);
    }

    private sealed class Row
    {
        public long Id { get; set; }
        public string TaskKey { get; set; } = "";
        public string Scope { get; set; } = "";
        public string Content { get; set; } = "";
        public DateTimeOffset CreatedAt { get; set; }

        public MemoryEntry ToEntity() => new(Id, TaskKey, Scope, Content, CreatedAt);
    }
}
