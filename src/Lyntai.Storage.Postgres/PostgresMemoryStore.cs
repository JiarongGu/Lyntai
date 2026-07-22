using System.Data;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Postgres;

/// <summary>
/// Task-scoped memory over <c>lyntai_memory_entry</c> + a <c>pg_trgm</c> GIN index. Same contract as the
/// SQLite/InMemory backends: dedup on remember, per-entry TTL, a configurable
/// <see cref="MemoryRetentionPolicy"/> (count cap + FIFO/LRU eviction, default TTL, size budget) applied via
/// the shared <see cref="MemoryEviction"/> helper, and fail-open recall (ILIKE substring, trigram-accelerated,
/// recency-ordered). Null parameters used in IS-NULL checks are cast (Npgsql can't infer the type otherwise).
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
        var policy = options.MemoryRetention;
        var expiresAt = (ttl ?? policy.DefaultTtl) is { } eff ? now + eff : (DateTimeOffset?)null; // per-call ttl wins
        using var conn = factory.Open();

        // dedup as a single ATOMIC upsert (via ux_lyntai_memory_dedup) — race-free. A refreshed fact bumps
        // recency + last-access + TTL.
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_entry (task_key, scope, content, created_at, last_accessed_at, expires_at)
            VALUES (@taskKey, @scope, @content, @now, @now, @expiresAt)
            ON CONFLICT (task_key, scope, md5(content)) DO UPDATE SET created_at = @now, last_accessed_at = @now, expires_at = @expiresAt
            """, new { taskKey, scope, content, now, expiresAt = (object?)expiresAt ?? DBNull.Value }, cancellationToken: ct)).ConfigureAwait(false);

        // policy-driven eviction via the shared helper (identical across backends). Manual = no size bound = skip.
        if (policy.HasSizeBound)
            await EvictAsync(conn, taskKey, scope, policy, now, ct).ConfigureAwait(false);
    }

    private static async Task EvictAsync(IDbConnection conn, string taskKey, string scope,
        MemoryRetentionPolicy policy, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<EvictRow>(new CommandDefinition("""
            SELECT id AS Id, created_at AS CreatedAt, COALESCE(last_accessed_at, created_at) AS LastAccessedAt,
                   expires_at AS ExpiresAt, LENGTH(content) AS Length
            FROM lyntai_memory_entry WHERE task_key = @taskKey AND scope = @scope
            """, new { taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
        var mapped = rows.Select(r => new MemoryEviction.Row(r.Id, r.CreatedAt, r.LastAccessedAt, r.ExpiresAt, r.Length)).ToList();
        var keep = MemoryEviction.Survivors(policy, mapped, now);
        var evict = mapped.Where(r => !keep.Contains(r.Id)).Select(r => r.Id).ToArray();
        if (evict.Length > 0)
            await conn.ExecuteAsync(new CommandDefinition(
                "DELETE FROM lyntai_memory_entry WHERE id = ANY(@evict)", new { evict }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemoryEntry>> RecallAsync(string taskKey, string? scope = null,
        string? query = null, int? limit = null, CancellationToken ct = default)
    {
        var take = limit ?? options.MemoryRecallLimit;
        var now = _clock();
        var touch = options.MemoryRetention.TracksAccess; // LRU refreshes last-access on recall
        try
        {
            using var conn = factory.Open();

            if (!string.IsNullOrWhiteSpace(query))
            {
                var pattern = LikePattern.Contains(query);
                var hits = (await conn.QueryAsync<Row>(new CommandDefinition($"""
                    SELECT {SelectColumns} FROM lyntai_memory_entry
                    WHERE task_key = @taskKey AND (@scope::text IS NULL OR scope = @scope)
                      AND (expires_at IS NULL OR expires_at > @now)
                      AND content ILIKE @pattern ESCAPE '\'
                    ORDER BY created_at DESC, id DESC LIMIT @take
                    """, new { taskKey, scope, pattern, take, now }, cancellationToken: ct)).ConfigureAwait(false))
                    .Select(r => r.ToEntity()).ToList();
                return await TouchAsync(conn, hits, touch, now, ct).ConfigureAwait(false);
            }

            var recent = (await conn.QueryAsync<Row>(new CommandDefinition($"""
                SELECT {SelectColumns} FROM lyntai_memory_entry
                WHERE task_key = @taskKey AND (@scope::text IS NULL OR scope = @scope)
                  AND (expires_at IS NULL OR expires_at > @now)
                ORDER BY created_at DESC, id DESC LIMIT @take
                """, new { taskKey, scope, take, now }, cancellationToken: ct)).ConfigureAwait(false))
                .Select(r => r.ToEntity()).ToList();
            return await TouchAsync(conn, recent, touch, now, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "memory recall failed for {Task}; returning empty (fail-open)", taskKey);
            return [];
        }
    }

    /// <summary>LRU: refresh last-access of the recalled entries so they survive eviction. Best-effort;
    /// only fires when the policy uses LRU (<paramref name="touch"/>).</summary>
    private static async Task<IReadOnlyList<MemoryEntry>> TouchAsync(IDbConnection conn, List<MemoryEntry> hits,
        bool touch, DateTimeOffset now, CancellationToken ct)
    {
        if (touch && hits.Count > 0)
        {
            var ids = hits.Select(h => h.Id).ToArray();
            await conn.ExecuteAsync(new CommandDefinition(
                "UPDATE lyntai_memory_entry SET last_accessed_at = @now WHERE id = ANY(@ids)",
                new { now, ids }, cancellationToken: ct)).ConfigureAwait(false);
        }
        return hits;
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

    private sealed class EvictRow
    {
        public long Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastAccessedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public int Length { get; set; }
    }
}
