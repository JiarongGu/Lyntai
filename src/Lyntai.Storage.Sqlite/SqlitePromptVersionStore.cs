using Dapper;
using Lyntai.Storage;

namespace Lyntai.Storage.Sqlite;

public sealed class SqlitePromptVersionStore(IDbConnectionFactory factory) : IPromptVersionStore
{
    // property-mapped row (not the record directly): Dapper's record-constructor matching wants exact
    // provider types, but version/is_active arrive as long from SQLite — property mapping converts.
    private const string SelectColumns =
        "name AS Name, version AS Version, template AS Template, author AS Author, created_at AS CreatedAt, is_active AS IsActive";

    public async Task<PromptVersion?> GetActiveAsync(string name, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var row = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM lyntai_prompt_version WHERE name = @name AND is_active = 1",
            new { name }, cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToEntity();
    }

    public async Task<PromptVersion> SaveAsync(string name, string template, string? author = null, CancellationToken ct = default)
    {
        var createdAt = DateTimeOffset.UtcNow;
        using var conn = factory.Open();
        using var tx = conn.BeginTransaction();

        var nextVersion = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(version), 0) + 1 FROM lyntai_prompt_version WHERE name = @name",
            new { name }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_prompt_version SET is_active = 0 WHERE name = @name AND is_active = 1",
            new { name }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_prompt_version (name, version, template, author, created_at, is_active)
            VALUES (@name, @nextVersion, @template, @author, @createdAt, 1)
            """, new { name, nextVersion, template, author, createdAt }, tx, cancellationToken: ct)).ConfigureAwait(false);

        tx.Commit();
        return new PromptVersion(name, nextVersion, template, author, createdAt, IsActive: true);
    }

    public async Task<IReadOnlyList<PromptVersion>> HistoryAsync(string name, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        var rows = await conn.QueryAsync<Row>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM lyntai_prompt_version WHERE name = @name ORDER BY version DESC",
            new { name }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToEntity())];
    }

    public async Task<PromptVersion?> RollbackAsync(string name, int version, CancellationToken ct = default)
    {
        using var conn = factory.Open();
        using var tx = conn.BeginTransaction();

        // re-activate an existing revision — history is never rewritten or deleted
        var target = await conn.QuerySingleOrDefaultAsync<Row>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM lyntai_prompt_version WHERE name = @name AND version = @version",
            new { name, version }, tx, cancellationToken: ct)).ConfigureAwait(false);
        if (target is null) { tx.Rollback(); return null; }

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_prompt_version SET is_active = 0 WHERE name = @name AND is_active = 1",
            new { name }, tx, cancellationToken: ct)).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_prompt_version SET is_active = 1 WHERE name = @name AND version = @version",
            new { name, version }, tx, cancellationToken: ct)).ConfigureAwait(false);

        tx.Commit();
        return target.ToEntity() with { IsActive = true };
    }

    private sealed class Row
    {
        public string Name { get; set; } = "";
        public int Version { get; set; }
        public string Template { get; set; } = "";
        public string? Author { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public bool IsActive { get; set; }

        public PromptVersion ToEntity() => new(Name, Version, Template, Author, CreatedAt, IsActive);
    }
}
