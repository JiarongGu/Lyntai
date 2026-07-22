using System.Data;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Storage.Sqlite;

/// <summary>Task-scoped memory over lyntai_memory_entry + the trigram FTS index. Size is bounded by the
/// configurable <see cref="MemoryRetentionPolicy"/> (count cap + FIFO/LRU eviction, default TTL, size
/// budget) via the shared <see cref="MemoryEviction"/> helper; recall is fail-open (degrades FTS → LIKE →
/// recent, returns empty on any storage fault rather than throwing).</summary>
public sealed class SqliteMemoryStore(
    IDbConnectionFactory factory,
    LyntaiOptions options,
    ILogger<SqliteMemoryStore>? logger = null,
    Func<DateTimeOffset>? clock = null) : IMemoryStore
{
    private const string SelectColumns =
        "m.id AS Id, m.task_key AS TaskKey, m.scope AS Scope, m.content AS Content, m.created_at AS CreatedAt";

    private readonly ILogger _logger = logger ?? NullLogger<SqliteMemoryStore>.Instance;
    // injectable so TTL/prune/LRU tests are deterministic — no DateTimeOffset.Now in the lifecycle logic
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task RememberAsync(string taskKey, string scope, string content, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var now = _clock();
        var policy = options.MemoryRetention;
        var expiresAt = (ttl ?? policy.DefaultTtl) is { } eff ? now + eff : (DateTimeOffset?)null; // per-call ttl wins over the policy default
        using var conn = factory.Open();

        // dedup as a single ATOMIC upsert (via ux_lyntai_memory_dedup) — race-free. An identical fact in the
        // same (task, scope) is refreshed (recency + last-access + TTL); the content is unchanged so the
        // (content-scoped) FTS trigger does not fire.
        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_memory_entry (task_key, scope, content, created_at, last_accessed_at, expires_at)
            VALUES (@taskKey, @scope, @content, @now, @now, @expiresAt)
            ON CONFLICT(task_key, scope, content) DO UPDATE SET created_at = @now, last_accessed_at = @now, expires_at = @expiresAt
            """, new { taskKey, scope, content, now, expiresAt }, cancellationToken: ct)).ConfigureAwait(false);

        // Eviction: the COUNT-CAP case (the common path) is a single ATOMIC DELETE — race-free and without
        // fetching the scope. The SIZE-BUDGET case needs the windowed cumulative-length compute, so it goes
        // through the shared MemoryEviction.ApplyAsync (fetch → survivors → delete). Manual = nothing.
        if (policy.MaxCharsPerScope is > 0)
            await MemoryEviction.ApplyAsync(policy, now,
                c => FetchScopedAsync(conn, taskKey, scope, c),
                (ids, c) => conn.ExecuteAsync(new CommandDefinition(
                    "DELETE FROM lyntai_memory_entry WHERE id IN @ids", new { ids }, cancellationToken: c)),
                ct).ConfigureAwait(false);
        else if (policy.MaxEntriesPerScope is int cap and > 0)
            await CapEvictAsync(conn, taskKey, scope, cap, policy.Eviction, now, ct).ConfigureAwait(false);
    }

    // Count-cap eviction as ONE atomic statement: keep the newest @cap LIVE entries (expired sort last, so
    // they're evicted first), recency by created_at (FIFO) or last_accessed_at (LRU). Reproduces
    // MemoryEviction.Survivors' count-cap branch, but race-free and without reading the scope into memory.
    private static Task CapEvictAsync(IDbConnection conn, string taskKey, string scope, int cap,
        MemoryEvictionMode mode, DateTimeOffset now, CancellationToken ct)
    {
        // `recency` is one of two fixed column expressions (no user input) — safe to interpolate.
        var recency = mode == MemoryEvictionMode.Lru ? "COALESCE(last_accessed_at, created_at)" : "created_at";
        return conn.ExecuteAsync(new CommandDefinition($"""
            DELETE FROM lyntai_memory_entry
            WHERE task_key = @taskKey AND scope = @scope AND id NOT IN (
                SELECT id FROM lyntai_memory_entry WHERE task_key = @taskKey AND scope = @scope
                ORDER BY (CASE WHEN expires_at IS NULL OR expires_at > @now THEN 0 ELSE 1 END),
                         {recency} DESC, id DESC
                LIMIT @cap)
            """, new { taskKey, scope, cap, now }, cancellationToken: ct));
    }

    private static async Task<IReadOnlyList<MemoryEviction.Row>> FetchScopedAsync(IDbConnection conn, string taskKey, string scope, CancellationToken ct)
    {
        var rows = await conn.QueryAsync<EvictRow>(new CommandDefinition("""
            SELECT id AS Id, created_at AS CreatedAt, COALESCE(last_accessed_at, created_at) AS LastAccessedAt,
                   expires_at AS ExpiresAt, LENGTH(content) AS Length
            FROM lyntai_memory_entry WHERE task_key = @taskKey AND scope = @scope
            """, new { taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => new MemoryEviction.Row(r.Id, r.CreatedAt, r.LastAccessedAt, r.ExpiresAt, r.Length))];
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
        // LRU refreshes last-access only on a QUERIED recall (a targeted lookup = "use"); a bare list-all
        // is enumeration, not use, so it must not bump every returned entry.
        var touch = options.MemoryRetention.TracksAccess && !string.IsNullOrWhiteSpace(query);
        try
        {
            using var conn = factory.Open();

            var match = FtsQuery.Build(query);
            if (match is not null)
            {
                try
                {
                    var hits = (await conn.QueryAsync<MemoryEntry>(new CommandDefinition($"""
                        SELECT {SelectColumns}
                        FROM lyntai_memory_fts JOIN lyntai_memory_entry m ON m.id = lyntai_memory_fts.rowid
                        WHERE lyntai_memory_fts MATCH @match AND m.task_key = @taskKey
                          AND (@scope IS NULL OR m.scope = @scope)
                          AND (m.expires_at IS NULL OR m.expires_at > @now)
                        ORDER BY bm25(lyntai_memory_fts) LIMIT @take
                        """, new { match, taskKey, scope, take, now }, cancellationToken: ct)).ConfigureAwait(false)).AsList();
                    if (hits.Count > 0) return await TouchAsync(conn, hits, touch, now, ct).ConfigureAwait(false);
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
                var likeHits = (await conn.QueryAsync<MemoryEntry>(new CommandDefinition($"""
                    SELECT {SelectColumns}
                    FROM lyntai_memory_entry m
                    WHERE m.task_key = @taskKey AND (@scope IS NULL OR m.scope = @scope)
                      AND (m.expires_at IS NULL OR m.expires_at > @now)
                      AND m.content LIKE @pattern ESCAPE '\'
                    ORDER BY m.created_at DESC, m.id DESC LIMIT @take
                    """, new { taskKey, scope, pattern, take, now }, cancellationToken: ct)).ConfigureAwait(false)).AsList();
                return await TouchAsync(conn, likeHits, touch, now, ct).ConfigureAwait(false);
            }

            var recent = (await conn.QueryAsync<MemoryEntry>(new CommandDefinition($"""
                SELECT {SelectColumns}
                FROM lyntai_memory_entry m
                WHERE m.task_key = @taskKey AND (@scope IS NULL OR m.scope = @scope)
                  AND (m.expires_at IS NULL OR m.expires_at > @now)
                ORDER BY m.created_at DESC, m.id DESC LIMIT @take
                """, new { taskKey, scope, take, now }, cancellationToken: ct)).ConfigureAwait(false)).AsList();
            return await TouchAsync(conn, recent, touch, now, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "memory recall failed for {Task}; returning empty (fail-open)", taskKey);
            return [];
        }
    }

    /// <summary>LRU: refresh last-access of the recalled entries so they survive eviction. Best-effort — a
    /// failed refresh (e.g. transient write contention) is swallowed so it NEVER turns a successful recall
    /// into an empty result (the outer catch is fail-open). Only fires on a queried LRU recall.</summary>
    private async Task<IReadOnlyList<MemoryEntry>> TouchAsync(IDbConnection conn, List<MemoryEntry> hits,
        bool touch, DateTimeOffset now, CancellationToken ct)
    {
        if (touch && hits.Count > 0)
        {
            try
            {
                var ids = hits.Select(h => h.Id).ToList();
                await conn.ExecuteAsync(new CommandDefinition(
                    "UPDATE lyntai_memory_entry SET last_accessed_at = @now WHERE id IN @ids",
                    new { now, ids }, cancellationToken: ct)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LRU last-access refresh failed for {Count} entries; recall result kept", hits.Count);
            }
        }
        return hits;
    }

    public async Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        await conn.ExecuteAsync(new CommandDefinition("""
            DELETE FROM lyntai_memory_entry WHERE task_key = @taskKey AND (@scope IS NULL OR scope = @scope)
            """, new { taskKey, scope }, cancellationToken: ct)).ConfigureAwait(false);
    }

    // Dapper materializes the eviction metadata into settable properties (a positional record struct maps
    // less predictably through the DateTimeOffset type handler), then we project to MemoryEviction.Row.
    private sealed class EvictRow
    {
        public long Id { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset LastAccessedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public int Length { get; set; }
    }
}
