using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Sqlite;

/// <summary>Task-scoped memory over lyntai_memory_entry + the trigram FTS index. Bounded (per-scope cap
/// trimmed on write) and fail-open (recall degrades FTS → LIKE → recent, and returns empty on any
/// storage fault rather than throwing).</summary>
public sealed class SqliteMemoryStore(
    IDbConnectionFactory factory,
    LyntaiOptions options,
    ILogger<SqliteMemoryStore>? logger = null) : IMemoryStore
{
    private const string SelectColumns =
        "m.id AS Id, m.task_key AS TaskKey, m.scope AS Scope, m.content AS Content, m.created_at AS CreatedAt";

    private readonly ILogger _logger = logger ?? NullLogger<SqliteMemoryStore>.Instance;

    public async Task RememberAsync(string taskKey, string scope, string content, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_entry (task_key, scope, content, created_at) VALUES (@taskKey, @scope, @content, @now)
            """, new { taskKey, scope, content, now = DateTimeOffset.UtcNow }, cancellationToken: ct)).ConfigureAwait(false);

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
        try
        {
            using var conn = factory.Open();

            var match = FtsQuery.Build(query);
            if (match is not null)
            {
                try
                {
                    var hits = await conn.QueryAsync<MemoryEntry>(new CommandDefinition($"""
                        SELECT {SelectColumns}
                        FROM lyntai_memory_fts JOIN lyntai_memory_entry m ON m.id = lyntai_memory_fts.rowid
                        WHERE lyntai_memory_fts MATCH @match AND m.task_key = @taskKey
                          AND (@scope IS NULL OR m.scope = @scope)
                        ORDER BY bm25(lyntai_memory_fts) LIMIT @take
                        """, new { match, taskKey, scope, take }, cancellationToken: ct)).ConfigureAwait(false);
                    var list = hits.AsList();
                    if (list.Count > 0) return list;
                    // no trigram hit → fall through to LIKE (covers punctuation-heavy queries)
                }
                catch (SqliteException ex)
                {
                    _logger.LogWarning(ex, "FTS recall failed for {Task}; falling back to LIKE", taskKey);
                }
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var pattern = "%" + query.Trim()
                    .Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
                var likeHits = await conn.QueryAsync<MemoryEntry>(new CommandDefinition($"""
                    SELECT {SelectColumns}
                    FROM lyntai_memory_entry m
                    WHERE m.task_key = @taskKey AND (@scope IS NULL OR m.scope = @scope)
                      AND m.content LIKE @pattern ESCAPE '\'
                    ORDER BY m.id DESC LIMIT @take
                    """, new { taskKey, scope, pattern, take }, cancellationToken: ct)).ConfigureAwait(false);
                return likeHits.AsList();
            }

            var recent = await conn.QueryAsync<MemoryEntry>(new CommandDefinition($"""
                SELECT {SelectColumns}
                FROM lyntai_memory_entry m
                WHERE m.task_key = @taskKey AND (@scope IS NULL OR m.scope = @scope)
                ORDER BY m.id DESC LIMIT @take
                """, new { taskKey, scope, take }, cancellationToken: ct)).ConfigureAwait(false);
            return recent.AsList();
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
            DELETE FROM lyntai_memory_entry WHERE task_key = @taskKey AND (@scope IS NULL OR scope = @scope)
            """, new { taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
    }
}
