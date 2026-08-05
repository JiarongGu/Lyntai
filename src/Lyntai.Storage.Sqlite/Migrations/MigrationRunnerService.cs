using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
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
    /// table dedups across passes. <see cref="StorageFeature.All"/> takes the fast path: one pass requesting
    /// only <see cref="StorageFeatures.AllTag"/>, which every migration carries — a migration that omits that
    /// tag never runs on the default path. A disabled feature's migration is never applied, so its table never
    /// lands.</summary>
    public static void MigrateUp(string dbPath, StorageFeature features)
    {
        // build the connection string safely (a raw $"Data Source={dbPath}" corrupts on a path with
        // ';' or '='); matches SqliteConnectionFactory's own builder-based construction.
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        SeedPragmas(connectionString);

        // the All-vs-subset tag dispatch lives in Core (StorageFeatures.TagPasses) so both backend
        // runners share the all-requested-tags-must-match semantics
        foreach (var tags in StorageFeatures.TagPasses(features))
            RunPass(connectionString, tags);
    }

    /// <summary>Migrate every domain's schema, awaitable — for an app owning its schema
    /// (<see cref="SchemaMigration.None"/>) from an async startup path. See
    /// <see cref="MigrateUpAsync(string, StorageFeature, CancellationToken)"/> for exactly what the
    /// <paramref name="ct"/> can and cannot do.</summary>
    public static Task MigrateUpAsync(string dbPath, CancellationToken ct = default) =>
        MigrateUpAsync(dbPath, StorageFeature.All, ct);

    /// <summary>The awaitable twin of <see cref="MigrateUp(string, StorageFeature)"/> — same passes, same
    /// tag dispatch, same schema.
    /// <para><b>What the token can do.</b> FluentMigrator's runner is SYNCHRONOUS and takes no
    /// <see cref="CancellationToken"/>, so <paramref name="ct"/> is honoured at the only two points that
    /// exist: BEFORE any work (a token already cancelled leaves the database file uncreated) and BETWEEN
    /// feature passes (each pass is a separate runner invocation whose applied versions the version table
    /// has already committed). <see cref="StorageFeature.All"/> is a SINGLE pass, so there the token
    /// effectively means "before starting" only.</para>
    /// <para><b>What it cannot do.</b> Cancel a migration in flight — once a pass begins, its DDL runs to
    /// completion. This method also runs INLINE on the calling thread and deliberately does not offload:
    /// a <c>Task.Run</c> wrapper would occupy a thread-pool thread for the whole migration and still be
    /// uncancellable, i.e. strictly worse than calling <see cref="MigrateUp(string, StorageFeature)"/>.
    /// The awaited work here is the pragma seed; the migration passes themselves are blocking.</para></summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the call, or
    /// between two feature passes.</exception>
    public static async Task MigrateUpAsync(string dbPath, StorageFeature features, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested(); // before ANY work — not even the db file is created
        var connectionString = new SqliteConnectionStringBuilder { DataSource = dbPath }.ToString();
        await SeedPragmasAsync(connectionString, ct).ConfigureAwait(false);

        foreach (var tags in StorageFeatures.TagPasses(features))
        {
            ct.ThrowIfCancellationRequested(); // a pass boundary is the last honest cancellation point
            RunPass(connectionString, tags);
        }
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

    /// <summary>The async form of <see cref="SeedPragmas"/> — same statements over the async ADO.NET calls,
    /// mirroring <c>SqliteConnectionFactory.OpenAsync</c>.</summary>
    private static async Task SeedPragmasAsync(string connectionString, CancellationToken ct)
    {
        await using var seed = new SqliteConnection(connectionString);
        await seed.OpenAsync(ct).ConfigureAwait(false);
        await using var pragma = seed.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        await pragma.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }

    private static void RunPass(string connectionString, string[] tags)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunnerService).Assembly).For.All()); // migrations + the lyntai_ version table
        // every pass is tag-filtered: TagPasses always yields a non-empty tag set (AllTag, or one feature tag)
        services.Configure<RunnerOptions>(opt => opt.Tags = tags);

        using var provider = services.BuildServiceProvider(validateScopes: false);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
