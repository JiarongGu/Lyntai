using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Lyntai.Storage.Relational;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `UsePostgresStorage` shows up right on the builder.
namespace Lyntai;

public static class PostgresStorageBuilderExtensions
{
    private static readonly RelationalBackend Postgres = new(
        nameof(UsePostgresStorage),
        "the response-cache and usage tables (lyntai_response_cache / lyntai_usage)",
        KeyValue: f => new PostgresKeyValueStore(f),
        PromptVersion: f => new PostgresPromptVersionStore(f),
        Conversation: f => new PostgresConversationStore(f),
        Memory: (f, sp) => new PostgresMemoryStore(f, sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<PostgresMemoryStore>>()),
        MemoryGraph: (f, sp) => new PostgresMemoryGraphStore(f, sp.GetService<ILogger<PostgresMemoryGraphStore>>()),
        Score: f => new PostgresScoreStore(f),
        Trace: f => new PostgresTraceStore(f),
        Jobs: (f, sp) => new PostgresJobStore(f, stepLogCap: sp.GetRequiredService<LyntaiOptions>().Jobs.MaxStepLog),
        CuratedMemory: (f, sp) => new PostgresCuratedMemoryStore(f, sp.GetService<ILogger<PostgresCuratedMemoryStore>>()));

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
    /// Default (<see cref="StorageFeature.All"/>) is the historical behavior.
    /// <para>Two wirings in one container — beside <c>UseSqliteStorage</c>, say — each keep their stores over
    /// their own database.</para></summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, string connectionString,
        StorageFeature features, SchemaMigration migration = SchemaMigration.OnStartup)
    {
        IDbConnectionFactory factory = new PostgresConnectionFactory(connectionString);
        if (migration == SchemaMigration.OnFirstUse)
            factory = new LazyMigratingConnectionFactory(factory,
                () => MigrationRunnerService.MigrateUp(connectionString, features));
        else if (migration == SchemaMigration.OnStartup)
            MigrationRunnerService.MigrateUp(connectionString, features);
        // SchemaMigration.None means the APP owns the schema, so no feature toggle decides what exists —
        // which is why that has to travel with the selection the Governance guard reads.
        return StoreWiring.Wire(builder, Postgres, factory, features, lyntaiMigrates: migration != SchemaMigration.None);
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
        StoreWiring.Wire(builder, Postgres, factory, features, lyntaiMigrates: false);

    // --- persistent backends for the front-door governance + semantic-memory seams --------------------
    // Mirror the SQLite ones: AddSingleton over the Core in-memory TryAdd defaults (win regardless of call
    // order), over the LAST Postgres wiring's factory.

    /// <summary>Back the response cache (<c>AddResponseCache</c>) with PostgreSQL (survives restarts, shared
    /// across processes). Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, SchemaMigration)"/>,
    /// including <see cref="StorageFeature.Governance"/> whenever Lyntai is the one migrating.</summary>
    public static LyntaiBuilder UsePostgresResponseCache(this LyntaiBuilder builder)
    {
        GovernanceGuard.Require(builder, Postgres, nameof(UsePostgresResponseCache));
        builder.Services.AddSingleton<Lyntai.Inference.Caching.IResponseCache>(sp => new PostgresResponseCache(
            StoreWiring.Factory(sp), sp.GetRequiredService<LyntaiOptions>()));
        return builder;
    }

    /// <summary>Back usage accounting (<c>AddUsageBudget</c>) with PostgreSQL (persistent, shared spend).
    /// Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, SchemaMigration)"/>, including
    /// <see cref="StorageFeature.Governance"/> whenever Lyntai is the one migrating.</summary>
    public static LyntaiBuilder UsePostgresUsageTracking(this LyntaiBuilder builder)
    {
        GovernanceGuard.Require(builder, Postgres, nameof(UsePostgresUsageTracking));
        builder.Services.AddSingleton<Lyntai.Inference.Budgeting.IUsageTracker>(sp =>
            new PostgresUsageTracker(StoreWiring.Factory(sp)));
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
        builder.Services.AddSingleton<Lyntai.Memory.IVectorStore>(sp => new PostgresVectorStore(StoreWiring.Factory(sp)));
        return builder;
    }
}
