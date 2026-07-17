using FluentMigrator.Runner;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>Discovers this assembly's migrations and applies them to a database.
/// Called by <c>UseSqliteStorage</c> before the stores are used.</summary>
public static class MigrationRunnerService
{
    public static void MigrateUp(string dbPath)
    {
        // build the connection string safely (a raw $"Data Source={dbPath}" corrupts on a path with
        // ';' or '='); matches SqliteConnectionFactory's own builder-based construction.
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();

        // set the family pragmas up front: WAL is a persistent header setting later connections inherit,
        // and a busy_timeout turns a momentary lock during migrate into a bounded wait, not an instant
        // "database is locked". FluentMigrator opens its own connection, so do this first.
        using (var seed = new SqliteConnection(connectionString))
        {
            seed.Open();
            using var pragma = seed.CreateCommand();
            pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }

        using var provider = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunnerService).Assembly).For.All()) // migrations + the lyntai_ version table
            .BuildServiceProvider(validateScopes: false);

        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
