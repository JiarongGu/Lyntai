using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `UseSqliteStorage` shows up right on the builder with no extra using.
namespace Lyntai;

public static class SqliteStorageBuilderExtensions
{
    /// <summary>Wire every storage domain to SQLite at <paramref name="dbPath"/>: registers the
    /// connection factory + all stores over Lyntai's own <c>lyntai_*</c> tables. Lyntai OWNS the LLM storage
    /// schema; an app attaches its own additional info via the record <c>metadata</c> fields rather than by
    /// managing tables. An app that genuinely needs its own backend registers its own domain-store impl
    /// (it wins — the domain stores register with <c>TryAdd</c>).
    /// <para><paramref name="migration"/> picks the schema-migration mode — see
    /// <see cref="SchemaMigration"/> (<c>OnStartup</c> default · <c>OnFirstUse</c> deferred ·
    /// <c>None</c> app-owned schema, e.g. via <see cref="MigrationRunnerService.MigrateUp(string)"/>).</para></summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, string dbPath,
        SchemaMigration migration = SchemaMigration.OnStartup) =>
        builder.UseSqliteStorage(dbPath, StorageFeature.All, migration);

    /// <summary>Wire only the SELECTED storage features to SQLite (feature toggles): a disabled feature
    /// registers no store AND lands no table (no unused <c>lyntai_*</c> tables for domains you don't use).
    /// Migration is per-feature (each migration is tagged with its feature); registration is gated per
    /// feature too, so a disabled domain's store isn't resolvable (its null-tolerant consumers skip it; a
    /// direct <c>GetRequiredService</c> throws — the startup signal that a disabled feature is being used).
    /// Default (<see cref="StorageFeature.All"/>) is the historical behavior.</summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, string dbPath, StorageFeature features,
        SchemaMigration migration = SchemaMigration.OnStartup)
    {
        IDbConnectionFactory factory;
        switch (migration)
        {
            case SchemaMigration.None:
                factory = new SqliteConnectionFactory(dbPath); // app owns the schema — no migrations
                break;
            case SchemaMigration.OnFirstUse:
                factory = new MigratingConnectionFactory(dbPath, features);
                break;
            default:
                var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                MigrationRunnerService.MigrateUp(dbPath, features);
                factory = new SqliteConnectionFactory(dbPath);
                break;
        }
        return builder.UseSqliteStorage(factory, features);
    }

    /// <summary>Wire every storage domain to SQLite using an APP-SUPPLIED <see cref="IDbConnectionFactory"/> —
    /// so the app owns connection creation, pooling, and lifecycle (e.g. a connection drawn from its own
    /// pool). Lyntai runs no migrations here; own the schema, or migrate on your own factory beforehand.
    /// The SQL is SQLite-dialect, so the factory must open SQLite connections.</summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, IDbConnectionFactory factory) =>
        builder.UseSqliteStorage(factory, StorageFeature.All);

    /// <summary>As <see cref="UseSqliteStorage(LyntaiBuilder, IDbConnectionFactory)"/>, but registers only
    /// the SELECTED features' stores (feature toggles over an app-supplied factory).</summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, IDbConnectionFactory factory, StorageFeature features)
    {
        // a Use*Governance-backed helper called BEFORE this one is caught here (see RequireGovernance)
        VerifyGovernanceBackedCalls(builder, features);
        builder.Services.AddSingleton(new SqliteFeatureSelection(features));
        builder.Services.AddSingleton(factory);
        // Register only the selected features. Domain stores use TryAdd so an app that registers its OWN
        // impl (a BYO backend) wins — before OR after UseSqliteStorage — matching Lyntai.Storage.InMemory and
        // the "anything you register wins" contract in the README.
        if (features.HasFlag(StorageFeature.KeyValue)) builder.Services.TryAddSingleton<IKeyValueStore, SqliteKeyValueStore>();
        if (features.HasFlag(StorageFeature.PromptVersion)) builder.Services.TryAddSingleton<IPromptVersionStore, SqlitePromptVersionStore>();
        if (features.HasFlag(StorageFeature.Conversation)) builder.Services.TryAddSingleton<IConversationStore, SqliteConversationStore>();
        if (features.HasFlag(StorageFeature.Memory))
            builder.Services.TryAddSingleton<IMemoryStore>(sp => new SqliteMemoryStore(
                sp.GetRequiredService<IDbConnectionFactory>(),
                sp.GetRequiredService<LyntaiOptions>(),
                sp.GetService<ILogger<SqliteMemoryStore>>()));
        if (features.HasFlag(StorageFeature.Score)) builder.Services.TryAddSingleton<IScoreStore, SqliteScoreStore>();
        if (features.HasFlag(StorageFeature.Trace)) builder.Services.TryAddSingleton<ITraceStore, SqliteTraceStore>();
        if (features.HasFlag(StorageFeature.Jobs))
            builder.Services.TryAddSingleton<IJobStore>(sp => new SqliteJobStore(
                sp.GetRequiredService<IDbConnectionFactory>(), stepLogCap: sp.GetRequiredService<LyntaiOptions>().Jobs.MaxStepLog));
        if (features.HasFlag(StorageFeature.CuratedMemory))
            builder.Services.TryAddSingleton<ICuratedMemoryStore>(sp => new SqliteCuratedMemoryStore(
                sp.GetRequiredService<IDbConnectionFactory>(), sp.GetService<ILogger<SqliteCuratedMemoryStore>>()));
        return builder;
    }

    // --- persistent backends for the front-door governance + semantic-memory seams --------------------
    // These override the in-memory defaults that AddResponseCache/AddUsageBudget/AddEmbeddings register in
    // Core (plain AddSingleton wins over their TryAdd regardless of call order). Each needs the SQLite
    // connection factory + schema from UseSqliteStorage, so call that first.

    /// <summary>Back the response cache (<c>AddResponseCache</c>) with SQLite so it survives restarts.
    /// Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, SchemaMigration)"/> for the factory +
    /// schema, including <see cref="StorageFeature.Governance"/> (see the Governance note below).</summary>
    public static LyntaiBuilder UseSqliteResponseCache(this LyntaiBuilder builder)
    {
        RequireGovernance(builder, nameof(UseSqliteResponseCache));
        builder.Services.AddSingleton<Lyntai.Llm.Caching.IResponseCache>(sp => new SqliteResponseCache(
            sp.GetRequiredService<IDbConnectionFactory>(), sp.GetRequiredService<LyntaiOptions>()));
        return builder;
    }

    /// <summary>Back usage accounting (<c>AddUsageBudget</c>) with SQLite so spend isn't reset every restart.
    /// Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, SchemaMigration)"/> for the factory +
    /// schema, including <see cref="StorageFeature.Governance"/> (see the Governance note below).</summary>
    public static LyntaiBuilder UseSqliteUsageTracking(this LyntaiBuilder builder)
    {
        RequireGovernance(builder, nameof(UseSqliteUsageTracking));
        builder.Services.AddSingleton<Lyntai.Llm.Budgeting.IUsageTracker>(sp => new SqliteUsageTracker(
            sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }

    /// <summary>Back semantic-memory vectors (<c>AddSemanticMemory</c> / <c>AddEmbeddings</c>) with SQLite
    /// so they survive restarts. Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, SchemaMigration)"/>
    /// for the factory + schema, including <see cref="StorageFeature.Governance"/> — the feature carrying
    /// the <c>lyntai_vector</c> table (see the Governance note below).</summary>
    public static LyntaiBuilder UseSqliteVectorStore(this LyntaiBuilder builder)
    {
        RequireGovernance(builder, nameof(UseSqliteVectorStore));
        builder.Services.AddSingleton<Lyntai.Memory.IVectorStore>(sp => new SqliteVectorStore(
            sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }

    // --- the Governance prerequisite, enforced at WIRING time -----------------------------------------
    // lyntai_response_cache, lyntai_usage and lyntai_vector all ship in the ONE Governance migration, so a
    // feature subset omitting StorageFeature.Governance leaves the three helpers above registering stores
    // over tables that were never created — and the app finds out at the first cached call / metered call /
    // recall, not at startup. UseSqliteStorage's stated contract is that a disabled domain is simply not
    // resolvable and that unresolvability IS the startup signal; these three are the only calls that could
    // break it, so they enforce it instead of degrading quietly.
    //
    // Order-independent by construction: the check needs BOTH the feature selection and the helper call, and
    // an app may write them either way round, so each side records a sentinel in the service collection and
    // verifies whatever the other side already recorded. Nothing ever resolves these sentinels — a guard you
    // can defeat by swapping two builder lines is not a guard.

    private sealed record SqliteFeatureSelection(StorageFeature Features);

    private sealed record SqliteGovernanceBackedCall(string Method);

    private static void RequireGovernance(LyntaiBuilder builder, string method)
    {
        builder.Services.AddSingleton(new SqliteGovernanceBackedCall(method));
        if (SelectedFeatures(builder) is { } features) VerifyGovernance(features, method);
    }

    private static void VerifyGovernanceBackedCalls(LyntaiBuilder builder, StorageFeature features)
    {
        foreach (var descriptor in builder.Services
                     .Where(d => !d.IsKeyedService && d.ServiceType == typeof(SqliteGovernanceBackedCall))
                     .ToList())
            VerifyGovernance(features, ((SqliteGovernanceBackedCall)descriptor.ImplementationInstance!).Method);
    }

    // the LAST selection wins, matching UseSqliteStorage's own last-registration-wins factory
    private static StorageFeature? SelectedFeatures(LyntaiBuilder builder) =>
        builder.Services.LastOrDefault(d => !d.IsKeyedService && d.ServiceType == typeof(SqliteFeatureSelection))
            ?.ImplementationInstance is SqliteFeatureSelection selection
            ? selection.Features
            : null;

    private static void VerifyGovernance(StorageFeature features, string method)
    {
        if (features.HasFlag(StorageFeature.Governance)) return;
        throw new InvalidOperationException(
            $"{method} needs StorageFeature.Governance, but UseSqliteStorage was called with a feature set that " +
            "omits it. Governance carries the response-cache, usage and vector tables (lyntai_response_cache / " +
            "lyntai_usage / lyntai_vector), so the store would be registered over a table that was never created " +
            $"and the failure would surface at the first call instead of here. Add StorageFeature.Governance to the " +
            $"UseSqliteStorage feature set, or drop the {method} call.");
    }
}
