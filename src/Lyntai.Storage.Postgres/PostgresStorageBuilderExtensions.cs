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
        return builder.UsePostgresStorage(factory, features);
    }

    /// <summary>Wire every storage domain to PostgreSQL using an APP-SUPPLIED
    /// <see cref="IDbConnectionFactory"/> — the app owns connection creation, pooling, and lifecycle.
    /// Lyntai runs no migrations here; own the schema, or migrate beforehand. The SQL is Postgres-dialect,
    /// so the factory must open Npgsql connections.</summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, IDbConnectionFactory factory) =>
        builder.UsePostgresStorage(factory, StorageFeature.All);

    /// <summary>As <see cref="UsePostgresStorage(LyntaiBuilder, IDbConnectionFactory)"/>, but registers only
    /// the SELECTED features' stores (feature toggles over an app-supplied factory).</summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, IDbConnectionFactory factory, StorageFeature features)
    {
        // a Governance-backed helper called BEFORE this one is caught here (see RequireGovernance)
        VerifyGovernanceBackedCalls(builder, features);
        builder.Services.AddSingleton(new PostgresFeatureSelection(features));
        builder.Services.AddSingleton(factory);
        // Register only the selected features. Domain stores use TryAdd so an app that registers its OWN
        // impl (a BYO backend) wins — before OR after UsePostgresStorage — matching Lyntai.Storage.Sqlite /
        // InMemory and the "anything you register wins" contract in the README.
        if (features.HasFlag(StorageFeature.KeyValue)) builder.Services.TryAddSingleton<IKeyValueStore, PostgresKeyValueStore>();
        if (features.HasFlag(StorageFeature.PromptVersion)) builder.Services.TryAddSingleton<IPromptVersionStore, PostgresPromptVersionStore>();
        if (features.HasFlag(StorageFeature.Conversation)) builder.Services.TryAddSingleton<IConversationStore, PostgresConversationStore>();
        if (features.HasFlag(StorageFeature.Memory))
            builder.Services.TryAddSingleton<IMemoryStore>(sp => new PostgresMemoryStore(
                sp.GetRequiredService<IDbConnectionFactory>(),
                sp.GetRequiredService<LyntaiOptions>(),
                sp.GetService<ILogger<PostgresMemoryStore>>()));
        if (features.HasFlag(StorageFeature.Score)) builder.Services.TryAddSingleton<IScoreStore, PostgresScoreStore>();
        if (features.HasFlag(StorageFeature.Trace)) builder.Services.TryAddSingleton<ITraceStore, PostgresTraceStore>();
        if (features.HasFlag(StorageFeature.Jobs))
            builder.Services.TryAddSingleton<IJobStore>(sp => new PostgresJobStore(
                sp.GetRequiredService<IDbConnectionFactory>(), stepLogCap: sp.GetRequiredService<LyntaiOptions>().Jobs.MaxStepLog));
        if (features.HasFlag(StorageFeature.CuratedMemory))
            builder.Services.TryAddSingleton<ICuratedMemoryStore>(sp => new PostgresCuratedMemoryStore(
                sp.GetRequiredService<IDbConnectionFactory>(), sp.GetService<ILogger<PostgresCuratedMemoryStore>>()));
        return builder;
    }

    // --- persistent backends for the front-door governance + semantic-memory seams --------------------
    // Mirror the SQLite ones: AddSingleton over the Core in-memory TryAdd defaults (win regardless of call
    // order). Each needs the connection factory + schema from UsePostgresStorage, so call that first.

    /// <summary>Back the response cache (<c>AddResponseCache</c>) with PostgreSQL (survives restarts, shared
    /// across processes). Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, SchemaMigration)"/>,
    /// including <see cref="StorageFeature.Governance"/> (see the Governance note below).</summary>
    public static LyntaiBuilder UsePostgresResponseCache(this LyntaiBuilder builder)
    {
        RequireGovernance(builder, nameof(UsePostgresResponseCache));
        builder.Services.AddSingleton<Lyntai.Llm.Caching.IResponseCache>(sp => new PostgresResponseCache(
            sp.GetRequiredService<IDbConnectionFactory>(), sp.GetRequiredService<LyntaiOptions>()));
        return builder;
    }

    /// <summary>Back usage accounting (<c>AddUsageBudget</c>) with PostgreSQL (persistent, shared spend).
    /// Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, SchemaMigration)"/>, including
    /// <see cref="StorageFeature.Governance"/> (see the Governance note below).</summary>
    public static LyntaiBuilder UsePostgresUsageTracking(this LyntaiBuilder builder)
    {
        RequireGovernance(builder, nameof(UsePostgresUsageTracking));
        builder.Services.AddSingleton<Lyntai.Llm.Budgeting.IUsageTracker>(sp => new PostgresUsageTracker(
            sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }

    /// <summary>Back semantic-memory vectors (<c>AddSemanticMemory</c> / <c>AddEmbeddings</c>) with
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
            sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }

    // --- the Governance prerequisite, enforced at WIRING time -----------------------------------------
    // lyntai_response_cache and lyntai_usage ship in the ONE Governance migration, so a feature subset
    // omitting StorageFeature.Governance leaves the two helpers above registering stores over tables that
    // were never created — and the app finds out at the first cached or metered call, not at startup.
    // UsePostgresStorage's stated contract is that a disabled domain is simply not resolvable and that
    // unresolvability IS the startup signal; these are the only calls that could break it, so they enforce
    // it instead of degrading quietly. (lyntai_vector is exempt — PostgresVectorStore creates its own.)
    //
    // Order-independent by construction: the check needs BOTH the feature selection and the helper call, and
    // an app may write them either way round, so each side records a sentinel in the service collection and
    // verifies whatever the other side already recorded. Nothing ever resolves these sentinels.
    // KEPT PARALLEL to the SQLite twin on purpose (see .claude/knowledge/storage.md) — the two backends'
    // builder extensions are deliberately not deduplicated.

    private sealed record PostgresFeatureSelection(StorageFeature Features);

    private sealed record PostgresGovernanceBackedCall(string Method);

    private static void RequireGovernance(LyntaiBuilder builder, string method)
    {
        builder.Services.AddSingleton(new PostgresGovernanceBackedCall(method));
        if (SelectedFeatures(builder) is { } features) VerifyGovernance(features, method);
    }

    private static void VerifyGovernanceBackedCalls(LyntaiBuilder builder, StorageFeature features)
    {
        foreach (var descriptor in builder.Services
                     .Where(d => !d.IsKeyedService && d.ServiceType == typeof(PostgresGovernanceBackedCall))
                     .ToList())
            VerifyGovernance(features, ((PostgresGovernanceBackedCall)descriptor.ImplementationInstance!).Method);
    }

    // the LAST selection wins, matching UsePostgresStorage's own last-registration-wins factory
    private static StorageFeature? SelectedFeatures(LyntaiBuilder builder) =>
        builder.Services.LastOrDefault(d => !d.IsKeyedService && d.ServiceType == typeof(PostgresFeatureSelection))
            ?.ImplementationInstance is PostgresFeatureSelection selection
            ? selection.Features
            : null;

    private static void VerifyGovernance(StorageFeature features, string method)
    {
        if (features.HasFlag(StorageFeature.Governance)) return;
        throw new InvalidOperationException(
            $"{method} needs StorageFeature.Governance, but UsePostgresStorage was called with a feature set that " +
            "omits it. Governance carries the response-cache and usage tables (lyntai_response_cache / " +
            "lyntai_usage), so the store would be registered over a table that was never created and the failure " +
            $"would surface at the first call instead of here. Add StorageFeature.Governance to the " +
            $"UsePostgresStorage feature set, or drop the {method} call.");
    }
}
