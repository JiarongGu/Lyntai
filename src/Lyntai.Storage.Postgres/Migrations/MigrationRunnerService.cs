using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
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
    /// only <see cref="StorageFeatures.AllTag"/>, which every migration carries — a migration that omits that
    /// tag never runs on the default path. A disabled feature's migration is never applied, so its table never
    /// lands.</summary>
    public static void MigrateUp(string connectionString, StorageFeature features)
    {
        // the All-vs-subset tag dispatch lives in Core (StorageFeatures.TagPasses) so both backend
        // runners share the all-requested-tags-must-match semantics
        foreach (var tags in StorageFeatures.TagPasses(features))
            RunPass(connectionString, tags);
    }

    /// <summary>Migrate every domain's schema, awaitable — for an app owning its schema
    /// (<see cref="SchemaMigration.None"/>) from an async startup path. See
    /// <see cref="MigrateUpAsync(string, StorageFeature, CancellationToken)"/> for exactly what the
    /// <paramref name="ct"/> can and cannot do.</summary>
    public static Task MigrateUpAsync(string connectionString, CancellationToken ct = default) =>
        MigrateUpAsync(connectionString, StorageFeature.All, ct);

    /// <summary>The awaitable twin of <see cref="MigrateUp(string, StorageFeature)"/> — same passes, same
    /// tag dispatch, same schema.
    /// <para><b>What the token can do.</b> FluentMigrator's runner is SYNCHRONOUS and takes no
    /// <see cref="CancellationToken"/>, so <paramref name="ct"/> is honoured at the only two points that
    /// exist: BEFORE any work (a token already cancelled never dials the connection string) and BETWEEN
    /// feature passes (each pass is a separate runner invocation whose applied versions the version table
    /// has already committed). <see cref="StorageFeature.All"/> is a SINGLE pass, so there the token
    /// effectively means "before starting" only.</para>
    /// <para><b>What it cannot do.</b> Cancel a migration in flight — once a pass begins, its DDL runs to
    /// completion. There is no async I/O to await on this path at all, so the whole call runs INLINE on the
    /// calling thread and returns an already-completed task; it deliberately does not offload, because a
    /// <c>Task.Run</c> wrapper would occupy a thread-pool thread for the whole migration and still be
    /// uncancellable — strictly worse than calling <see cref="MigrateUp(string, StorageFeature)"/>. The
    /// awaitable exists so an async startup path composes without <c>GetAwaiter().GetResult()</c>.</para></summary>
    /// <exception cref="OperationCanceledException"><paramref name="ct"/> was cancelled before the call, or
    /// between two feature passes.</exception>
    public static Task MigrateUpAsync(string connectionString, StorageFeature features, CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested(); // before ANY work — the connection string is never dialled
            foreach (var tags in StorageFeatures.TagPasses(features))
            {
                ct.ThrowIfCancellationRequested(); // a pass boundary is the last honest cancellation point
                RunPass(connectionString, tags);
            }
            return Task.CompletedTask;
        }
        // no await point exists on this path, so faults would otherwise surface SYNCHRONOUSLY from a
        // Task-returning method — a trap for a caller that stores the task before awaiting it
        catch (OperationCanceledException ex) when (ex.CancellationToken.IsCancellationRequested)
        {
            return Task.FromCanceled(ex.CancellationToken);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    private static void RunPass(string connectionString, string[] tags)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddPostgres()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunnerService).Assembly).For.All()); // migrations + the lyntai_ version table
        // every pass is tag-filtered: a yielded pass always carries a tag (AllTag, or one feature tag) —
        // and StorageFeature.None yields NO passes, so this runner never executes for it (no tables, no
        // version table)
        services.Configure<RunnerOptions>(opt => opt.Tags = tags);

        using var provider = services.BuildServiceProvider(validateScopes: false);
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMigrationRunner>().MigrateUp();
    }
}
