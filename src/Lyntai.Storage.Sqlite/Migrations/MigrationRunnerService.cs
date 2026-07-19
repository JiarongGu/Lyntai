using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Lyntai.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Discovers this assembly's migrations and applies them to a database.
/// Called by <c>UseSqliteStorage</c> before the stores are used.</summary>
public static class MigrationRunnerService
{
    /// <summary>Migrate every domain's schema (the default).</summary>
    public static void MigrateUp(string dbPath) => MigrateUp(dbPath, StorageFeature.All);

    /// <summary>Migrate only the SELECTED features' tables. Each migration is tagged with its feature
    /// (<c>[Tags(nameof(StorageFeature.X))]</c>). FluentMigrator runs a migration only when the runner's
    /// requested tags are ALL present on it, so a SUBSET is applied one feature (tag) per pass — the version
    /// table dedups across passes. <see cref="StorageFeature.All"/> takes the fast path: one pass with no tag
    /// filter (an empty requested-set is a subset of every migration's tags → all run), i.e. the historical
    /// behavior. A disabled feature's migration is never applied, so its table never lands.</summary>
    public static void MigrateUp(string dbPath, StorageFeature features)
    {
        // build the connection string safely (a raw $"Data Source={dbPath}" corrupts on a path with
        // ';' or '='); matches SqliteConnectionFactory's own builder-based construction.
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        SeedPragmas(connectionString);

        if (features == StorageFeature.All)
            RunPass(connectionString, tags: [StorageFeatures.AllTag]); // one pass — every migration carries AllTag
        else
            foreach (var tag in StorageFeatures.TagsFor(features))
                RunPass(connectionString, tags: [tag]); // one feature per pass (all-requested-tags-must-match)
    }

    private static void SeedPragmas(string connectionString)
    {
        // WAL is a persistent header setting later connections inherit; a busy_timeout turns a momentary
        // lock during migrate into a bounded wait, not an instant "database is locked". FluentMigrator opens
        // its own connection, so do this first.
        using var seed = new SqliteConnection(connectionString);
        seed.Open();
        using var pragma = seed.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        pragma.ExecuteNonQuery();
    }

    private static void RunPass(string connectionString, string[]? tags)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunnerService).Assembly).For.All()); // migrations + the lyntai_ version table
        if (tags is not null)
            services.Configure<RunnerOptions>(opt => opt.Tags = tags);

        using var provider = services.BuildServiceProvider(validateScopes: false);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
