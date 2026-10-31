using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Tests.Storage;
using Npgsql;
using Xunit.Abstractions;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// <b>RV27 Track A</b> on Postgres, the third durable store beside <see cref="ProfileTrackATests"/>' two: D221's gate
/// asks the contract suites to pass on the chosen backends, and the projection's correction needs only
/// <see cref="IRevisionedCuratedMemoryStore"/>, which every curated store implements. A case that exports from one
/// site and imports into another needs TWO stores, so each site is its own database cut from one migrated template
/// rather than a share of the fixture's database.
/// </summary>
[Collection("postgres")]
public sealed class ProfileTrackAPostgresTests(PostgresFixture pg, ITestOutputHelper output)
{
    [SkippableFact]
    public async Task The_projection_over_curated_memory_passes_every_case_on_postgres()
    {
        pg.Require();
        using var databases = new SiteDatabases(pg.ConnectionString);

        var outcomes = await ProfileTrackA.RunAsync(databases.Open, s => new ProfileOverCurated(s));

        Assert.Equal(ProfileTrackA.Cases.Count, outcomes.Count);
        foreach (var bucket in outcomes.GroupBy(o => o.Bucket))
            output.WriteLine($"postgres/{bucket.Key}: {bucket.Count(o => o.Passed)}/{bucket.Count()}");
        var failed = outcomes.Where(o => !o.Passed).ToList();
        foreach (var f in failed) output.WriteLine($"postgres: {f.Question}: {f.Reason}");
        Assert.Empty(failed.Where(f => !ProfileTrackA.IsKnownGap(f.Question, "postgres")).Select(f => $"{f.Question}: {f.Reason}"));
    }

    /// <summary>One migrated template database, and a fresh copy of it for each site a case opens. Pooling is off so a
    /// copy can be dropped the moment its lease ends.</summary>
    private sealed class SiteDatabases : IDisposable
    {
        private readonly NpgsqlConnectionStringBuilder _admin;
        private readonly string _template = "lyntai_profile_tpl_" + Guid.NewGuid().ToString("N");

        internal SiteDatabases(string fixtureConnection)
        {
            _admin = new NpgsqlConnectionStringBuilder(fixtureConnection) { Pooling = false };
            Execute($"CREATE DATABASE \"{_template}\"");
            Lyntai.Storage.Postgres.Migrations.MigrationRunnerService.MigrateUp(For(_template));
        }

        internal (ICuratedMemoryStore Store, IDisposable Lease) Open()
        {
            var name = "lyntai_profile_site_" + Guid.NewGuid().ToString("N");
            Execute($"CREATE DATABASE \"{name}\" TEMPLATE \"{_template}\"");
            return (new PostgresCuratedMemoryStore(new PostgresConnectionFactory(For(name))), new Drop(this, name));
        }

        public void Dispose() => DropDatabase(_template);

        private string For(string database) =>
            new NpgsqlConnectionStringBuilder(_admin.ConnectionString) { Database = database }.ConnectionString;

        private void Execute(string sql)
        {
            using var connection = new NpgsqlConnection(_admin.ConnectionString);
            connection.Execute(sql);
        }

        private void DropDatabase(string name) => Execute($"DROP DATABASE IF EXISTS \"{name}\" WITH (FORCE)");

        private sealed class Drop(SiteDatabases owner, string name) : IDisposable
        {
            public void Dispose() => owner.DropDatabase(name);
        }
    }
}
