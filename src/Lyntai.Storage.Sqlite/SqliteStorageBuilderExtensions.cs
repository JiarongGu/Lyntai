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
        // SchemaMigration.None means the APP owns the schema, so no feature toggle decides what exists —
        // see the Governance note below for why that has to travel with the selection.
        return WireStores(builder, factory, features, lyntaiMigrates: migration != SchemaMigration.None);
    }

    /// <summary>Wire every storage domain to SQLite using an APP-SUPPLIED <see cref="IDbConnectionFactory"/> —
    /// so the app owns connection creation, pooling, and lifecycle (e.g. a connection drawn from its own
    /// pool). Lyntai runs no migrations here; own the schema, or migrate on your own factory beforehand.
    /// The SQL is SQLite-dialect, so the factory must open SQLite connections.</summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, IDbConnectionFactory factory) =>
        builder.UseSqliteStorage(factory, StorageFeature.All);

    /// <summary>As <see cref="UseSqliteStorage(LyntaiBuilder, IDbConnectionFactory)"/>, but registers only
    /// the SELECTED features' stores (feature toggles over an app-supplied factory).</summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, IDbConnectionFactory factory, StorageFeature features) =>
        // an app-supplied factory means Lyntai runs no migrations, so the Governance check does not apply
        WireStores(builder, factory, features, lyntaiMigrates: false);

    private static LyntaiBuilder WireStores(LyntaiBuilder builder, IDbConnectionFactory factory,
        StorageFeature features, bool lyntaiMigrates)
    {
        var selection = new SqliteFeatureSelection(features, lyntaiMigrates);
        // a Use*Governance-backed helper called BEFORE this one is caught here (see RequireGovernance)
        VerifyGovernanceBackedCalls(builder, selection);
        builder.Services.AddSingleton(selection);
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
    /// schema, including <see cref="StorageFeature.Governance"/> whenever Lyntai is the one migrating
    /// (see the Governance note below).</summary>
    public static LyntaiBuilder UseSqliteResponseCache(this LyntaiBuilder builder)
    {
        RequireGovernance(builder, nameof(UseSqliteResponseCache));
        builder.Services.AddSingleton<Lyntai.Llm.Caching.IResponseCache>(sp => new SqliteResponseCache(
            sp.GetRequiredService<IDbConnectionFactory>(), sp.GetRequiredService<LyntaiOptions>()));
        return builder;
    }

    /// <summary>Back usage accounting (<c>AddUsageBudget</c>) with SQLite so spend isn't reset every restart.
    /// Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, SchemaMigration)"/> for the factory +
    /// schema, including <see cref="StorageFeature.Governance"/> whenever Lyntai is the one migrating
    /// (see the Governance note below).</summary>
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
    /// the <c>lyntai_vector</c> table — whenever Lyntai is the one migrating (see the Governance note
    /// below).</summary>
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
    //
    // It applies ONLY where Lyntai owns the schema, which is why the selection carries LyntaiMigrates. Under
    // SchemaMigration.None or an app-supplied IDbConnectionFactory, Lyntai runs no migrations at all: the
    // feature set no longer decides which tables exist, the app's own DDL does. Firing there would reject a
    // wiring that has always worked (an app that created lyntai_vector itself and passed a narrow feature
    // set), and the remedy the message offers — add StorageFeature.Governance — would create no table anyway.
    //
    // CONSEQUENCE WORTH KNOWING: a BYO-factory call made LAST disables the guard for the whole wiring.
    //   UseSqliteStorage(path, Memory, SchemaMigration.OnStartup)   // Lyntai migrates; guard is live
    //   UseSqliteStorage(factory, Memory)                           // app owns the schema; guard stands down
    //   UseSqliteVectorStore()                                      // no longer rejected
    // That is defensible — the app supplied the factory, and it supplied it LAST, so the app owns the schema
    // and there is no migration left for the guard to speak about. It is nonetheless an interaction a host can
    // trip by REORDERING two lines it thought were independent, so it is written down rather than discovered.
    // The rule to hold on to: the guard follows the SELECTION, and a BYO factory is a selection that says
    // "not Lyntai's schema". If both overloads are called, the last one decides — for the guard exactly as for
    // the connection factory itself.

    private sealed record SqliteFeatureSelection(StorageFeature Features, bool LyntaiMigrates);

    private sealed record SqliteGovernanceBackedCall(string Method);

    private static void RequireGovernance(LyntaiBuilder builder, string method)
    {
        builder.Services.AddSingleton(new SqliteGovernanceBackedCall(method));
        if (Selection(builder) is { } selection) VerifyGovernance(selection, method);
    }

    private static void VerifyGovernanceBackedCalls(LyntaiBuilder builder, SqliteFeatureSelection selection)
    {
        foreach (var descriptor in builder.Services
                     .Where(d => !d.IsKeyedService && d.ServiceType == typeof(SqliteGovernanceBackedCall))
                     .ToList())
            VerifyGovernance(selection, ((SqliteGovernanceBackedCall)descriptor.ImplementationInstance!).Method);
    }

    // The last selection registered SO FAR — which, because the guard is evaluated EAGERLY (at each call,
    // not once at the end), is not necessarily the selection the app finishes with.
    //
    // The difference is observable, so state it rather than imply otherwise: with
    //   UseSqliteStorage(a, Memory) → UseSqliteVectorStore() → UseSqliteStorage(b, All)
    // the helper throws against the Memory selection even though the FINAL selection is valid. A lazy guard
    // judging only the end state was considered and rejected: the check is symmetric by construction — each
    // side records a sentinel and verifies whatever the other side already recorded — and deferring it needs a
    // run-once-after-configure hook on LyntaiBuilder, i.e. a new public extension point in Core existing solely
    // to serve two adapters, plus a new way for the guard to silently not run at all. Eager also fails AT the
    // offending line, which is the property the guard was added for.
    // The cost accepted: re-stating the feature set across two UseSqliteStorage calls, narrow first, is
    // rejected. State the feature set once — or make the widening call before the helper.
    private static SqliteFeatureSelection? Selection(LyntaiBuilder builder) =>
        builder.Services.LastOrDefault(d => !d.IsKeyedService && d.ServiceType == typeof(SqliteFeatureSelection))
            ?.ImplementationInstance as SqliteFeatureSelection;

    private static void VerifyGovernance(SqliteFeatureSelection selection, string method)
    {
        if (!selection.LyntaiMigrates) return;   // the app owns the schema — no migration was going to run
        if (selection.Features.HasFlag(StorageFeature.Governance)) return;
        throw new InvalidOperationException(
            $"{method} needs StorageFeature.Governance, but UseSqliteStorage was called with a feature set that " +
            "omits it. Governance carries the response-cache, usage and vector tables (lyntai_response_cache / " +
            "lyntai_usage / lyntai_vector), so the store would be registered over a table that was never created " +
            $"and the failure would surface at the first call instead of here. Add StorageFeature.Governance to the " +
            $"UseSqliteStorage feature set, or drop the {method} call.");
    }
}
