using Lyntai.Storage;
using Lyntai.Storage.Relational;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `UseSqliteStorage` shows up right on the builder with no extra using.
namespace Lyntai;

public static class SqliteStorageBuilderExtensions
{
    private static readonly RelationalBackend Sqlite = new(
        nameof(UseSqliteStorage),
        "the response-cache, usage and vector tables (lyntai_response_cache / lyntai_usage / lyntai_vector)",
        KeyValue: f => new SqliteKeyValueStore(f),
        PromptVersion: f => new SqlitePromptVersionStore(f),
        Conversation: f => new SqliteConversationStore(f),
        Memory: (f, sp) => new SqliteMemoryStore(f, sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<SqliteMemoryStore>>()),
        MemoryGraph: (f, sp) => new SqliteMemoryGraphStore(f, sp.GetService<ILogger<SqliteMemoryGraphStore>>()),
        Score: f => new SqliteScoreStore(f),
        Trace: f => new SqliteTraceStore(f),
        Jobs: (f, sp) => new SqliteJobStore(f, stepLogCap: sp.GetRequiredService<LyntaiOptions>().Jobs.MaxStepLog),
        CuratedMemory: (f, sp) => new SqliteCuratedMemoryStore(f, sp.GetService<ILogger<SqliteCuratedMemoryStore>>()));

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
    /// Default (<see cref="StorageFeature.All"/>) is the historical behavior.
    /// <para>Two wirings in one container — say memory in one file and the rest in another, or beside
    /// <c>UsePostgresStorage</c> — each keep their stores over their own database.</para></summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, string dbPath, StorageFeature features,
        SchemaMigration migration = SchemaMigration.OnStartup)
    {
        IDbConnectionFactory factory = new SqliteConnectionFactory(dbPath);
        if (migration == SchemaMigration.OnFirstUse)
            factory = new LazyMigratingConnectionFactory(factory, () => MigrationRunnerService.MigrateUp(dbPath, features));
        else if (migration == SchemaMigration.OnStartup)
            MigrationRunnerService.MigrateUp(dbPath, features);
        // SchemaMigration.None means the APP owns the schema, so no feature toggle decides what exists —
        // which is why that has to travel with the selection the Governance guard reads.
        return StoreWiring.Wire(builder, Sqlite, factory, features, lyntaiMigrates: migration != SchemaMigration.None);
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
        StoreWiring.Wire(builder, Sqlite, factory, features, lyntaiMigrates: false);

    // --- persistent backends for the front-door governance + semantic-memory seams --------------------
    // These override the in-memory defaults that AddResponseCache/AddUsageBudget register in Core (plain
    // AddSingleton wins over their TryAdd regardless of call order), over the LAST SQLite wiring's factory.
    // Each needs StorageFeature.Governance whenever Lyntai is the one migrating (GovernanceGuard, D150).

    /// <summary>Back the response cache (<c>AddResponseCache</c>) with SQLite so it survives restarts.
    /// Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, SchemaMigration)"/> for the factory +
    /// schema, including <see cref="StorageFeature.Governance"/> whenever Lyntai is the one migrating.</summary>
    public static LyntaiBuilder UseSqliteResponseCache(this LyntaiBuilder builder)
    {
        GovernanceGuard.Require(builder, Sqlite, nameof(UseSqliteResponseCache));
        builder.Services.AddSingleton<Lyntai.Inference.Caching.IResponseCache>(sp => new SqliteResponseCache(
            StoreWiring.Factory(sp), sp.GetRequiredService<LyntaiOptions>()));
        return builder;
    }

    /// <summary>Back usage accounting (<c>AddUsageBudget</c>) with SQLite so spend isn't reset every restart.
    /// Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, SchemaMigration)"/> for the factory +
    /// schema, including <see cref="StorageFeature.Governance"/> whenever Lyntai is the one migrating.</summary>
    public static LyntaiBuilder UseSqliteUsageTracking(this LyntaiBuilder builder)
    {
        GovernanceGuard.Require(builder, Sqlite, nameof(UseSqliteUsageTracking));
        builder.Services.AddSingleton<Lyntai.Inference.Budgeting.IUsageTracker>(sp =>
            new SqliteUsageTracker(StoreWiring.Factory(sp)));
        return builder;
    }

    /// <summary>Back semantic-memory vectors (<c>AddSemanticMemory</c> / <c>AddProvider</c>) with SQLite
    /// so they survive restarts. Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, SchemaMigration)"/>
    /// for the factory + schema, including <see cref="StorageFeature.Governance"/> — the feature carrying
    /// the <c>lyntai_vector</c> table — whenever Lyntai is the one migrating.</summary>
    public static LyntaiBuilder UseSqliteVectorStore(this LyntaiBuilder builder)
    {
        GovernanceGuard.Require(builder, Sqlite, nameof(UseSqliteVectorStore));
        builder.Services.AddSingleton<Lyntai.Memory.IVectorStore>(sp => new SqliteVectorStore(StoreWiring.Factory(sp)));
        return builder;
    }
}
