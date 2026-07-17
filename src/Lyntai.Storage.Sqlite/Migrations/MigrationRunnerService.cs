using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Discovers this assembly's migrations and applies them to a database.
/// Called by <c>UseSqliteStorage</c> before the stores are used.</summary>
public static class MigrationRunnerService
{
    public static void MigrateUp(string dbPath)
    {
        using var provider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString($"Data Source={dbPath}")
                .ScanIn(typeof(MigrationRunnerService).Assembly).For.All()) // migrations + the lyntai_ version table
            .BuildServiceProvider(validateScopes: false);

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
