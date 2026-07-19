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
    /// <summary>Wire every storage domain to PostgreSQL.
    /// <para><paramref name="migrateOnFirstUse"/> defers migrations to the first store access so DI
    /// composition does no I/O.</para>
    /// <para><paramref name="migrate"/>=false makes the APP own the schema — Lyntai runs no migrations,
    /// assuming the <c>lyntai_*</c> tables already exist. Every object is <c>lyntai_</c>-prefixed, so the
    /// connection may target an existing application database.</para></summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, string connectionString,
        bool migrateOnFirstUse = false, bool migrate = true) =>
        builder.UsePostgresStorage(connectionString, StorageFeature.All, migrateOnFirstUse, migrate);

    /// <summary>Wire only the SELECTED storage features to PostgreSQL (feature toggles): a disabled feature
    /// registers no store AND lands no table (no unused <c>lyntai_*</c> tables for domains you don't use).
    /// Migration is per-feature (each migration is tagged with its feature); registration is gated per
    /// feature too, so a disabled domain's store isn't resolvable (its null-tolerant consumers skip it; a
    /// direct <c>GetRequiredService</c> throws — the startup signal that a disabled feature is being used).
    /// Default (<see cref="StorageFeature.All"/>) is the historical behavior.</summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, string connectionString,
        StorageFeature features, bool migrateOnFirstUse = false, bool migrate = true)
    {
        IDbConnectionFactory factory;
        if (!migrate)
        {
            factory = new PostgresConnectionFactory(connectionString); // app owns the schema
        }
        else if (migrateOnFirstUse)
        {
            factory = new MigratingConnectionFactory(connectionString, features);
        }
        else
        {
            MigrationRunnerService.MigrateUp(connectionString, features);
            factory = new PostgresConnectionFactory(connectionString);
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
            builder.Services.TryAddSingleton<ICuratedMemoryStore>(sp => new PostgresCuratedMemoryStore(sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }

    // --- persistent backends for the front-door governance + semantic-memory seams --------------------
    // Mirror the SQLite ones: AddSingleton over the Core in-memory TryAdd defaults (win regardless of call
    // order). Each needs the connection factory + schema from UsePostgresStorage, so call that first.

    /// <summary>Back the response cache (<c>AddResponseCache</c>) with PostgreSQL (survives restarts, shared
    /// across processes). Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, bool, bool)"/>.</summary>
    public static LyntaiBuilder UsePostgresResponseCache(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<Lyntai.Llm.Caching.IResponseCache>(sp => new PostgresResponseCache(
            sp.GetRequiredService<IDbConnectionFactory>(), sp.GetRequiredService<LyntaiOptions>()));
        return builder;
    }

    /// <summary>Back usage accounting (<c>AddUsageBudget</c>) with PostgreSQL (persistent, shared spend).
    /// Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, bool, bool)"/>.</summary>
    public static LyntaiBuilder UsePostgresUsageTracking(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<Lyntai.Llm.Budgeting.IUsageTracker>(sp => new PostgresUsageTracker(
            sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }

    /// <summary>Back semantic-memory vectors (<c>AddEmbeddings</c>) with pgvector — the similarity search
    /// runs in the database (cosine <c>&lt;=&gt;</c> + SQL top-k), not brute-force in the app. Creates its
    /// <c>vector</c> extension + table lazily on first use (so this is the only thing that needs pgvector).
    /// Requires <see cref="UsePostgresStorage(LyntaiBuilder, string, bool, bool)"/> for the factory.</summary>
    public static LyntaiBuilder UsePostgresVectorStore(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<Lyntai.Memory.IVectorStore>(sp => new PostgresVectorStore(
            sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }
}
