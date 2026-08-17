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
        // Same race, same remedy as AppendMessageAsync's MAX(seq)+1: under READ COMMITTED two concurrent
        // saves can read the same MAX(version), and UNIQUE(name, version) rejects the loser — retried HERE
        // (bounded), recomputing in a fresh transaction, so the caller never sees a raw unique-violation
        // for a transient race. (SQLite's immediate transaction serializes writers; InMemory locks.)
        var createdAt = DateTimeOffset.UtcNow;
        await using var conn = await factory.OpenAsync(ct).ConfigureAwait(false);
        for (var attempt = 0; ; attempt++)
        {
            using var tx = conn.BeginTransaction();
            try
            {
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

                tx.Commit();
                return new PromptVersion(name, nextVersion, template, author, createdAt, IsActive: true);
            }
            catch (Npgsql.PostgresException ex) when (ex.SqlState == "23505" && attempt < 3)
            {
                tx.Rollback(); // a concurrent save won the version — recompute against what it committed
            }
        }
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
        using var tx = conn.BeginTransaction();

        var target = await conn.QuerySingleOrDefaultAsync<PromptVersionRow>(new CommandDefinition(
            $"SELECT {SelectColumns} FROM lyntai_prompt_version WHERE name = @name AND version = @version",
            new { name, version }, tx, cancellationToken: ct)).ConfigureAwait(false);
        if (target is null) { tx.Rollback(); return null; }

        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_prompt_version SET is_active = FALSE WHERE name = @name AND is_active",
            new { name }, tx, cancellationToken: ct)).ConfigureAwait(false);
        await conn.ExecuteAsync(new CommandDefinition(
            "UPDATE lyntai_prompt_version SET is_active = TRUE WHERE name = @name AND version = @version",
            new { name, version }, tx, cancellationToken: ct)).ConfigureAwait(false);

        tx.Commit();
        return target.ToEntity() with { IsActive = true };
    }

}
