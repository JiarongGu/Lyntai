using Dapper;
using Lyntai.Storage;

namespace Lyntai.Storage.Sqlite;

/// <summary>SQLite <see cref="ICuratedMemoryStore"/> over <c>lyntai_curated_memory</c>. Small managed
/// catalog — plain CRUD, no FTS/cap/TTL. Timestamps are TEXT (the shared DateTimeOffset handler);
/// <c>enabled</c> is an INTEGER bool.</summary>
public sealed class SqliteCuratedMemoryStore(IDbConnectionFactory factory, Func<DateTimeOffset>? clock = null) : ICuratedMemoryStore
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
            """, new { kind, content, source, enabled, now }, cancellationToken: ct)).ConfigureAwait(false);
    }

    public async Task<bool> UpdateAsync(long id, string? content = null, bool? enabled = null, string? source = null, CancellationToken ct = default)
    {
        var now = _clock();
        using var conn = factory.Open();
        // COALESCE: only the provided (non-null) fields change; null leaves the column as-is
        var n = await conn.ExecuteAsync(new CommandDefinition("""
            UPDATE lyntai_curated_memory
            SET content = COALESCE(@content, content),
                enabled = COALESCE(@enabled, enabled),
                source  = COALESCE(@source, source),
                updated_at = @now
            WHERE id = @id
            """, new { id, content, enabled, source, now }, cancellationToken: ct)).ConfigureAwait(false);
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
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {Cols} FROM lyntai_curated_memory WHERE id = @id", new { id }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToRecord();
    }

    public async Task<IReadOnlyList<CuratedMemory>> ListAsync(string? kind = null, bool enabledOnly = false, int? limit = null, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<Row>(new CommandDefinition($"""
            SELECT {Cols} FROM lyntai_curated_memory
            WHERE (@kind IS NULL OR kind = @kind) AND (@enabledOnly = 0 OR enabled = 1)
            ORDER BY kind, created_at, id
            LIMIT @limit
            """, new { kind, enabledOnly, limit = limit ?? -1 }, cancellationToken: ct)).ConfigureAwait(false);
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

        public CuratedMemory ToRecord() => new(Id, Kind, Content, Source, Enabled, CreatedAt, UpdatedAt);
    }
}
