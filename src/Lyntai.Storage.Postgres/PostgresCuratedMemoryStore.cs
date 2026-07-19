using Dapper;
using Lyntai.Storage;

namespace Lyntai.Storage.Postgres;

/// <summary>PostgreSQL <see cref="ICuratedMemoryStore"/> over <c>lyntai_curated_memory</c>. Plain CRUD
/// (no FTS/cap/TTL); <c>enabled</c> is a native BOOLEAN and timestamps are <c>timestamptz</c>. Nullable
/// update params carry <c>::</c> casts so a NULL "leave unchanged" resolves its type.</summary>
public sealed class PostgresCuratedMemoryStore(IDbConnectionFactory factory, Func<DateTimeOffset>? clock = null) : ICuratedMemoryStore
{
    private const string Cols = "id, kind, content, source, enabled, created_at, updated_at";
    private readonly Func<DateTimeOffset> _clock = clock ?? (() => DateTimeOffset.UtcNow);

    public async Task<long> AddAsync(string kind, string content, string? source = null, bool enabled = true, CancellationToken ct = default)
    {
        var now = _clock();
        using var conn = factory.Open();
        return await conn.ExecuteScalarAsync<long>(new CommandDefinition("""
            INSERT INTO lyntai_curated_memory (kind, content, source, enabled, created_at, updated_at)
            VALUES (@kind, @content, @source, @enabled, @now, @now)
            RETURNING id
            """, new { kind, content, source = (object?)source ?? DBNull.Value, enabled, now }, cancellationToken: ct)).ConfigureAwait(false);
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

    public async Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false, int? limit = null, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        // @limit NULL → LIMIT ALL (no cap); enabledOnly is a plain bool predicate
        var rows = await conn.QueryAsync<CuratedMemory>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (@kind::text IS NULL OR kind = @kind) AND (NOT @enabledOnly OR enabled)
            -- COLLATE "C" (byte-ordinal) so the text sort matches SQLite's default BINARY collation rather
            -- than the Postgres DB locale collation — identical curated list order across backends.
            ORDER BY kind COLLATE "C", created_at, id
            LIMIT @limit
            """, new
        {
            kind = (object?)kind ?? DBNull.Value, enabledOnly,
            limit = (object?)limit ?? DBNull.Value,
        }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows];
    }
}
