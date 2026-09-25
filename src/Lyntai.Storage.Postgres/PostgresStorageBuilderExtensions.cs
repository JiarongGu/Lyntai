using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `UsePostgresStorage` shows up right on the builder.
namespace Lyntai;

public static class PostgresStorageBuilderExtensions
{
    /// <summary>Wire every storage domain to PostgreSQL. Every object is <c>lyntai_</c>-prefixed, so the
    /// connection may target an existing application database.
    /// <para><paramref name="migration"/> picks the schema-migration mode — see
    /// <see cref="SchemaMigration"/> (<c>OnStartup</c> default · <c>OnFirstUse</c> deferred ·
    /// <c>None</c> app-owned schema, e.g. via <see cref="MigrationRunnerService.MigrateUp(string)"/>).</para></summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, string connectionString,
        SchemaMigration migration = SchemaMigration.OnStartup) =>
        builder.UsePostgresStorage(connectionString, StorageFeature.All, migration);

    /// <summary>Wire only the SELECTED storage features to PostgreSQL (feature toggles): a disabled feature
    /// registers no store AND lands no table (no unused <c>lyntai_*</c> tables for domains you don't use).
    /// Migration is per-feature (each migration is tagged with its feature); registration is gated per
    /// feature too, so a disabled domain's store isn't resolvable (its null-tolerant consumers skip it; a
    /// direct <c>GetRequiredService</c> throws — the startup signal that a disabled feature is being used).
    /// Default (<see cref="StorageFeature.All"/>) is the historical behavior.</summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, string connectionString,
        StorageFeature features, SchemaMigration migration = SchemaMigration.OnStartup)
    {
        IDbConnectionFactory factory;
        switch (migration)
        {
            case SchemaMigration.None:
                factory = new PostgresConnectionFactory(connectionString); // app owns the schema
                break;
            case SchemaMigration.OnFirstUse:
                factory = new MigratingConnectionFactory(connectionString, features);
                break;
            default:
                MigrationRunnerService.MigrateUp(connectionString, features);
                factory = new PostgresConnectionFactory(connectionString);
                break;
        }
        // SchemaMigration.None means the APP owns the schema, so no feature toggle decides what exists —
        // see the Governance note below for why that has to travel with the selection.
        return WireStores(builder, factory, features, lyntaiMigrates: migration != SchemaMigration.None);
    }

    /// <summary>Wire every storage domain to PostgreSQL using an APP-SUPPLIED
    /// <see cref="IDbConnectionFactory"/> — the app owns connection creation, pooling, and lifecycle.
    /// Lyntai runs no migrations here; own the schema, or migrate beforehand. The SQL is Postgres-dialect,
    /// so the factory must open Npgsql connections.</summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, IDbConnectionFactory factory) =>
        builder.UsePostgresStorage(factory, StorageFeature.All);

    /// <summary>As <see cref="UsePostgresStorage(LyntaiBuilder, IDbConnectionFactory)"/>, but registers only
    /// the SELECTED features' stores (feature toggles over an app-supplied factory).</summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, IDbConnectionFactory factory, StorageFeature features) =>
        // an app-supplied factory means Lyntai runs no migrations, so the Governance check does not apply
        WireStores(builder, factory, features, lyntaiMigrates: false);

    private static LyntaiBuilder WireStores(LyntaiBuilder builder, IDbConnectionFactory factory,
        StorageFeature features, bool lyntaiMigrates)
    {
        var selection = new PostgresFeatureSelection(features, lyntaiMigrates, factory);
        // a Governance-backed helper called BEFORE this one is caught here (see RequireGovernance)
        VerifyGovernanceBackedCalls(builder, selection);
        builder.Services.AddSingleton(selection);
        builder.Services.TryAddSingleton(factory);
        // Register only the selected features. Domain stores use TryAdd so an app that registers its OWN
        // impl (a BYO backend) wins — before OR after UsePostgresStorage — matching Lyntai.Storage.Sqlite /
        // InMemory and the "anything you register wins" contract in the README. Each is built over THIS
        // wiring's factory, never the container's: stores are first-wins, so a factory resolved at run time
        // would hand them whichever wiring registered last.
        if (features.HasFlag(StorageFeature.KeyValue)) builder.Services.TryAddSingleton<IKeyValueStore>(_ => new PostgresKeyValueStore(factory));
        if (features.HasFlag(StorageFeature.PromptVersion)) builder.Services.TryAddSingleton<IPromptVersionStore>(_ => new PostgresPromptVersionStore(factory));
        if (features.HasFlag(StorageFeature.Conversation)) builder.Services.TryAddSingleton<IConversationStore>(_ => new PostgresConversationStore(factory));
        if (features.HasFlag(StorageFeature.Memory))
        {
            builder.Services.TryAddSingleton<IMemoryStore>(sp => new PostgresMemoryStore(
                factory, sp.GetRequiredService<LyntaiOptions>(), sp.GetService<ILogger<PostgresMemoryStore>>()));
            // the graph tables ship under the same feature tag as the keyword log
            builder.Services.TryAddSingleton<Lyntai.Memory.IMemoryGraphStore>(sp => new PostgresMemoryGraphStore(
                factory, sp.GetService<ILogger<PostgresMemoryGraphStore>>()));
        }
        if (features.HasFlag(StorageFeature.Score)) builder.Services.TryAddSingleton<IScoreStore>(_ => new PostgresScoreStore(factory));
        if (features.HasFlag(StorageFeature.Trace)) builder.Services.TryAddSingleton<ITraceStore>(_ => new PostgresTraceStore(factory));
        if (features.HasFlag(StorageFeature.Jobs))
            builder.Services.TryAddSingleton<IJobStore>(sp => new PostgresJobStore(
                factory, stepLogCap: sp.GetRequiredService<LyntaiOptions>().Jobs.MaxStepLog));
        if (features.HasFlag(StorageFeature.CuratedMemory))
            builder.Services.TryAddSingleton<ICuratedMemoryStore>(sp => new PostgresCuratedMemoryStore(
                factory, sp.GetService<ILogger<PostgresCuratedMemoryStore>>()));
        return builder;
    }

    // The factory of this backend's LAST wiring — the same one the Governance check reads — or, with no
    // wiring at all, an app-registered factory.
    private static IDbConnectionFactory Factory(IServiceProvider sp) =>
        sp.GetService<PostgresFeatureSelection>()?.Factory ?? sp.GetRequiredService<IDbConnectionFactory>();

    // --- persistent backends for the front-door governance + semantic-memory seams --------------------
    // Mirror the SQLite ones: AddSingleton over the Core in-memory TryAdd defaults (win regardless of call
    // order). Each needs the connection factory + schema from UsePostgresStorage, so call that first.

    /// <summary>Back the response cache (<c>AddResponseCache</c>) with PostgreSQL (survives restarts, shared
    /// across processes). Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, SchemaMigration)"/>,
    /// including <see cref="StorageFeature.Governance"/> whenever Lyntai is the one migrating (see the
    /// Governance note below).</summary>
    public static LyntaiBuilder UsePostgresResponseCache(this LyntaiBuilder builder)
    {
        RequireGovernance(builder, nameof(UsePostgresResponseCache));
        builder.Services.AddSingleton<Lyntai.Inference.Caching.IResponseCache>(sp => new PostgresResponseCache(
            Factory(sp), sp.GetRequiredService<LyntaiOptions>()));
        return builder;
    }

    /// <summary>Back usage accounting (<c>AddUsageBudget</c>) with PostgreSQL (persistent, shared spend).
    /// Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, SchemaMigration)"/>, including
    /// <see cref="StorageFeature.Governance"/> whenever Lyntai is the one migrating (see the Governance
    /// note below).</summary>
    public static LyntaiBuilder UsePostgresUsageTracking(this LyntaiBuilder builder)
    {
        RequireGovernance(builder, nameof(UsePostgresUsageTracking));
        builder.Services.AddSingleton<Lyntai.Inference.Budgeting.IUsageTracker>(sp => new PostgresUsageTracker(
            Factory(sp)));
        return builder;
    }

    /// <summary>Back semantic-memory vectors (<c>AddSemanticMemory</c> / <c>AddProvider</c>) with
    /// pgvector — the similarity search
    /// runs in the database (cosine <c>&lt;=&gt;</c> + SQL top-k), not brute-force in the app. Creates its
    /// <c>vector</c> extension + table lazily on first use (so this is the only thing that needs pgvector).
    /// Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, SchemaMigration)"/> for the factory.
    /// <para>Deliberately NOT subject to the <see cref="StorageFeature.Governance"/> check its SQLite
    /// counterpart enforces: this store creates its own schema on first use rather than relying on the
    /// Governance migration, so a feature subset omitting Governance leaves it perfectly functional.</para></summary>
    public static LyntaiBuilder UsePostgresVectorStore(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<Lyntai.Memory.IVectorStore>(sp => new PostgresVectorStore(
            Factory(sp)));
        return builder;
    }

    // --- the Governance prerequisite, enforced at WIRING time -----------------------------------------
    // lyntai_response_cache, lyntai_usage and lyntai_vector all ship in the ONE Governance migration, so a
    // feature subset omitting it leaves the three helpers above registering stores over tables that were
    // never created. Why the check is EAGER, what a lazy one would have accepted, and the two scope rules
    // (order-independent across the storage/helper PAIR only; applies only where Lyntai owns the schema) are
    // docs/DECISIONS.md D150.

    private sealed record PostgresFeatureSelection(StorageFeature Features, bool LyntaiMigrates, IDbConnectionFactory Factory);

    private sealed record PostgresGovernanceBackedCall(string Method);

    private static void RequireGovernance(LyntaiBuilder builder, string method)
    {
        builder.Services.AddSingleton(new PostgresGovernanceBackedCall(method));
        if (Selection(builder) is { } selection) VerifyGovernance(selection, method);
    }

    private static void VerifyGovernanceBackedCalls(LyntaiBuilder builder, PostgresFeatureSelection selection)
    {
        foreach (var descriptor in builder.Services
                     .Where(d => !d.IsKeyedService && d.ServiceType == typeof(PostgresGovernanceBackedCall))
                     .ToList())
            VerifyGovernance(selection, ((PostgresGovernanceBackedCall)descriptor.ImplementationInstance!).Method);
    }

    // The last selection registered SO FAR — the guard is EAGER, so this is not necessarily the selection
    // the app finishes with. That difference is deliberate and priced in docs/DECISIONS.md D150.
    private static PostgresFeatureSelection? Selection(LyntaiBuilder builder) =>
        builder.Services.LastOrDefault(d => !d.IsKeyedService && d.ServiceType == typeof(PostgresFeatureSelection))
            ?.ImplementationInstance as PostgresFeatureSelection;

    private static void VerifyGovernance(PostgresFeatureSelection selection, string method)
    {
        if (!selection.LyntaiMigrates) return;   // the app owns the schema — no migration was going to run
        if (selection.Features.HasFlag(StorageFeature.Governance)) return;
        throw new InvalidOperationException(
            $"{method} needs StorageFeature.Governance, but UsePostgresStorage was called with a feature set that " +
            "omits it. Governance carries the response-cache and usage tables (lyntai_response_cache / " +
            "lyntai_usage), so the store would be registered over a table that was never created and the failure " +
            $"would surface at the first call instead of here. Add StorageFeature.Governance to the " +
            $"UsePostgresStorage feature set, or drop the {method} call.");
    }
}
