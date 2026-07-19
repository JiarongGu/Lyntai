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
    ILogger<SqliteMemoryStore>? logger = null,
    Func<DateTimeOffset>? clock = null) : IMemoryStore
{
    private const string SelectColumns =
        "m.id AS Id, m.task_key AS TaskKey, m.scope AS Scope, m.content AS Content, m.created_at AS CreatedAt";

    private readonly ILogger _logger = logger ?? NullLogger<SqliteMemoryStore>.Instance;
    // injectable so TTL/prune tests are deterministic — no DateTimeOffset.Now in the lifecycle logic
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var now = _clock();
        var expiresAt = ttl is null ? (DateTimeOffset?)null : now + ttl.Value;
        using var conn = factory.Open();

        // dedup as a single ATOMIC upsert (via the ux_lyntai_memory_dedup unique index) — race-free, unlike
        // a UPDATE-then-INSERT two concurrent Remembers could both fall through and duplicate. An identical
        // fact in the same (task, scope) is refreshed (recency + TTL); the AFTER UPDATE trigger re-syncs FTS.
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_entry (task_key, scope, content, created_at, expires_at)
            VALUES (@taskKey, @scope, @content, @now, @expiresAt)
            ON CONFLICT(task_key, scope, content) DO UPDATE SET created_at = @now, expires_at = @expiresAt
            """, new { taskKey, scope, content, now, expiresAt }, cancellationToken: ct)).ConfigureAwait(false);

        // bounded: keep the newest @cap LIVE entries, trim the rest. Expired entries sort last so they
        // are evicted BEFORE still-valid ones (else the cap could delete live facts while keeping dead
        // ones); recency is by created_at so a re-remembered (refreshed) fact ranks as newest.
        await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_entry
            WHERE task_key = @taskKey AND scope = @scope AND id NOT IN (
                SELECT id FROM lyntai_memory_entry WHERE task_key = @taskKey AND scope = @scope
                ORDER BY (CASE WHEN expires_at IS NULL OR expires_at > @now THEN 0 ELSE 1 END),
                         created_at DESC, id DESC
                LIMIT @cap)
            """, new { taskKey, scope, cap = options.MemoryCapPerScope, now }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<int> PruneAsync(string? taskKey = null, TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        var now = _clock();
        var cutoff = olderThan is null ? (DateTimeOffset?)null : now - olderThan.Value;
        using var conn = factory.Open();
        return await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_entry
            WHERE (@taskKey IS NULL OR task_key = @taskKey)
              AND ( (expires_at IS NOT NULL AND expires_at <= @now)
                    OR (@cutoff IS NOT NULL AND created_at < @cutoff) )
            """, new { taskKey, now, cutoff }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        var take = limit ?? options.MemoryRecallLimit;
        var now = _clock(); // expired entries (@now past expires_at) are never returned
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
                          AND (m.expires_at IS NULL OR m.expires_at > @now)
                        ORDER BY bm25(lyntai_memory_fts) LIMIT @take
                        """, new { match, taskKey, scope, take, now }, cancellationToken: ct)).ConfigureAwait(false);
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
                var pattern = LikePattern.Contains(query);
                var likeHits = await conn.QueryAsync<MemoryEntry>(new CommandDefinition($"""
                    SELECT {SelectColumns}
                    FROM lyntai_memory_entry m
                    WHERE m.task_key = @taskKey AND (@scope IS NULL OR m.scope = @scope)
                      AND (m.expires_at IS NULL OR m.expires_at > @now)
                      AND m.content LIKE @pattern ESCAPE '\'
                    ORDER BY m.created_at DESC, m.id DESC LIMIT @take
                    """, new { taskKey, scope, pattern, take, now }, cancellationToken: ct)).ConfigureAwait(false);
                return likeHits.AsList();
            }

            var recent = await conn.QueryAsync<MemoryEntry>(new CommandDefinition($"""
                SELECT {SelectColumns}
                FROM lyntai_memory_entry m
                WHERE m.task_key = @taskKey AND (@scope IS NULL OR m.scope = @scope)
                  AND (m.expires_at IS NULL OR m.expires_at > @now)
                ORDER BY m.created_at DESC, m.id DESC LIMIT @take
                """, new { taskKey, scope, take, now }, cancellationToken: ct)).ConfigureAwait(false);
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
