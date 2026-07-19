using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Lyntai.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Discovers this assembly's migrations and applies them to a PostgreSQL database.</summary>
public static class MigrationRunnerService
{
    /// <summary>Migrate every domain's schema (the default).</summary>
    public static void MigrateUp(string connectionString) => MigrateUp(connectionString, StorageFeature.All);

    /// <summary>Migrate only the SELECTED features' tables. Each migration is tagged with its feature
    /// (<c>[Tags(nameof(StorageFeature.X))]</c>). FluentMigrator runs a migration only when the runner's
    /// requested tags are ALL present on it, so a SUBSET is applied one feature (tag) per pass — the version
    /// table dedups across passes. <see cref="StorageFeature.All"/> takes the fast path: one pass requesting
    /// only <see cref="StorageFeatures.AllTag"/>, which every migration carries. A disabled feature's
    /// migration is never applied, so its table never lands.</summary>
    public static void MigrateUp(string connectionString, StorageFeature features)
    {
        if (features == StorageFeature.All)
            RunPass(connectionString, tags: [StorageFeatures.AllTag]); // one pass — every migration carries AllTag
        else
            foreach (var tag in StorageFeatures.TagsFor(features))
                RunPass(connectionString, tags: [tag]); // one feature per pass (all-requested-tags-must-match)
    }

    private static void RunPass(string connectionString, string[]? tags)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunnerService).Assembly).For.All()); // migrations + the lyntai_ version table
        if (tags is not null)
            services.Configure<RunnerOptions>(opt => opt.Tags = tags);

        using var provider = services.BuildServiceProvider(validateScopes: false);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
