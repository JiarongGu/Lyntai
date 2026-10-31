using System.Collections.Concurrent;
using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Tests.Storage;
using Npgsql;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// One migrated template database per Postgres container, and a fresh copy of it for each SITE a case opens. A case that
/// exports from one site and imports into another needs two stores, and a crash-and-reopen case needs one it can
/// abandon, so each site is its own database rather than a share of the fixture's. Pooling is off so a copy can be
/// dropped the moment its lease ends; the template is left to the container, which is disposed with the collection.
/// </summary>
internal sealed class ProfilePostgresSites
{
    private static readonly ConcurrentDictionary<string, Lazy<ProfilePostgresSites>> Cache = new();

    private readonly NpgsqlConnectionStringBuilder _admin;
    private readonly string _template = "lyntai_profile_tpl_" + Guid.NewGuid().ToString("N");

    private ProfilePostgresSites(string fixtureConnection)
    {
        _admin = new NpgsqlConnectionStringBuilder(fixtureConnection) { Pooling = false };
        Execute($"CREATE DATABASE \"{_template}\"");
        Lyntai.Storage.Postgres.Migrations.MigrationRunnerService.MigrateUp(For(_template));
    }

    /// <summary>The sites of the container <paramref name="pg"/> started, or a SKIP when it never did.</summary>
    internal static ProfilePostgresSites For(PostgresFixture pg)
    {
        pg.Require();
        return Cache.GetOrAdd(pg.ConnectionString, c => new Lazy<ProfilePostgresSites>(() => new ProfilePostgresSites(c))).Value;
    }

    internal (ICuratedMemoryStore Store, IDisposable Lease) Open()
    {
        var name = "lyntai_profile_site_" + Guid.NewGuid().ToString("N");
        Execute($"CREATE DATABASE \"{name}\" TEMPLATE \"{_template}\"");
        return (new PostgresCuratedMemoryStore(new PostgresConnectionFactory(For(name))), new Drop(this, name));
    }

    private string For(string database) =>
        new NpgsqlConnectionStringBuilder(_admin.ConnectionString) { Database = database }.ConnectionString;

    private void Execute(string sql)
    {
        using var connection = new NpgsqlConnection(_admin.ConnectionString);
        connection.Execute(sql);
    }

    private sealed class Drop(ProfilePostgresSites owner, string name) : IDisposable
    {
        public void Dispose() => owner.Execute($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");
    }
}
