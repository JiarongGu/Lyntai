using System.Data.Common;
using Dapper;

namespace Lyntai.Storage.Postgres;

public sealed class PostgresPromptVersionStore(IDbConnectionFactory factory) : IPromptVersionStore
{
    private const string SelectColumns =
        "name AS Name, version AS Version, template AS Template, author AS Author, created_at AS CreatedAt, is_active AS IsActive";

    public async Task<PromptVersion?> GetActiveAsync(string name, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var row = await conn.QuerySingleOrDefaultAsync<PromptVersionRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM lyntai_prompt_version WHERE name = @name AND is_active", new { name },
            cancellationToken: ct)).ConfigureAwait(false);
        return row?.ToEntity();
    }

    public async Task<PromptVersion> SaveAsync(string name, string template, string? author = null, CancellationToken ct = default)
    {
        var createdAt = DateTimeOffset.UtcNow;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SerializeAsync(conn, tx, name, ct).ConfigureAwait(false);

        var nextVersion = await conn.ExecuteScalarAsync<int>(new CommandDefinition(
            "SELECT COALESCE(MAX(version), 0) + 1 FROM lyntai_prompt_version WHERE name = @name",
            new { name }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_prompt_version SET is_active = FALSE WHERE name = @name AND is_active",
            new { name }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await conn.ExecuteAsync(new CommandDefinition("""
            INSERT INTO lyntai_prompt_version (name, version, template, author, created_at, is_active)
            VALUES (@name, @nextVersion, @template, @author, @createdAt, TRUE)
            """, new { name, nextVersion, template, author, createdAt }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return new PromptVersion(name, nextVersion, template, author, createdAt, IsActive: true);
    }

    public async Task<IReadOnlyList<PromptVersion>> HistoryAsync(string name, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        var rows = await conn.QueryAsync<PromptVersionRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM lyntai_prompt_version WHERE name = @name ORDER BY version DESC",
            new { name }, cancellationToken: ct)).ConfigureAwait(false);
        return [.. rows.Select(r => r.ToEntity())];
    }

    public async Task<PromptVersion?> RollbackAsync(string name, int version, CancellationToken ct = default)
    {
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        await using var tx = await conn.BeginTransactionAsync(ct).ConfigureAwait(false);
        await SerializeAsync(conn, tx, name, ct).ConfigureAwait(false);

        var target = await conn.QuerySingleOrDefaultAsync<PromptVersionRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM lyntai_prompt_version WHERE name = @name AND version = @version",
            new { name, version }, tx, cancellationToken: ct)).ConfigureAwait(false);
        if (target is null) { await tx.RollbackAsync(ct).ConfigureAwait(false); return null; }

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_prompt_version SET is_active = FALSE WHERE name = @name AND is_active",
            new { name }, tx, cancellationToken: ct)).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_prompt_version SET is_active = TRUE WHERE name = @name AND version = @version",
            new { name, version }, tx, cancellationToken: ct)).ConfigureAwait(false);

        await tx.CommitAsync(ct).ConfigureAwait(false);
        return target.ToEntity() with { IsActive = true };
    }

    // Serializes every writer of ONE name until its transaction ends. Under READ COMMITTED a deactivating
    // UPDATE that waited on another writer re-checks only the rows it already saw, so without this a racing
    // save and rollback (or two rollbacks) each leave a revision active — and a MAX(version) read races too.
    private static Task SerializeAsync(DbConnection conn, DbTransaction tx, string name, CancellationToken ct) =>
        conn.ExecuteAsync(new CommandDefinition(
            "SELECT pg_advisory_xact_lock(hashtext('lyntai_prompt:' || @name))",
            new { name }, tx, cancellationToken: ct));
}
