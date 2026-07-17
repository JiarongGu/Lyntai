using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Storage.Postgres.Migrations;

/// <summary>Discovers this assembly's migrations and applies them to a PostgreSQL database.</summary>
public static class MigrationRunnerService
{
    public static void MigrateUp(string connectionString)
    {
        using var provider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunnerService).Assembly).For.All()) // migrations + the lyntai_ version table
            .BuildServiceProvider(validateScopes: false);

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
