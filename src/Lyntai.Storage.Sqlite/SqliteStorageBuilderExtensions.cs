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
    /// <para><paramref name="migrateOnFirstUse"/> defers migrations to the first store access so DI
    /// composition does no I/O (AOT/startup-sensitive hosts, container health checks).</para>
    /// <para><paramref name="migrate"/>=false makes the APP own the schema — Lyntai runs no migrations,
    /// assuming the tables (see the <c>lyntai_*</c> schema) already exist. Run
    /// <see cref="MigrationRunnerService.MigrateUp(string)"/> yourself if you want Lyntai's schema on your own
    /// terms.</para></summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, string dbPath,
        bool migrateOnFirstUse = false, bool migrate = true) =>
        builder.UseSqliteStorage(dbPath, StorageFeature.All, migrateOnFirstUse, migrate);

    /// <summary>Wire only the SELECTED storage features to SQLite (feature toggles): a disabled feature
    /// registers no store AND lands no table (no unused <c>lyntai_*</c> tables for domains you don't use).
    /// Migration is per-feature (each migration is tagged with its feature); registration is gated per
    /// feature too, so a disabled domain's store isn't resolvable (its null-tolerant consumers skip it; a
    /// direct <c>GetRequiredService</c> throws — the startup signal that a disabled feature is being used).
    /// Default (<see cref="StorageFeature.All"/>) is the historical behavior.</summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, string dbPath, StorageFeature features,
        bool migrateOnFirstUse = false, bool migrate = true)
    {
        IDbConnectionFactory factory;
        if (!migrate)
        {
            factory = new SqliteConnectionFactory(dbPath); // app owns the schema — no migrations
        }
        else if (migrateOnFirstUse)
        {
            factory = new MigratingConnectionFactory(dbPath, features);
        }
        else
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            MigrationRunnerService.MigrateUp(dbPath, features);
            factory = new SqliteConnectionFactory(dbPath);
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
            builder.Services.TryAddSingleton<ICuratedMemoryStore>(sp => new SqliteCuratedMemoryStore(sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }

    // --- persistent backends for the front-door governance + semantic-memory seams --------------------
    // These override the in-memory defaults that AddResponseCache/AddUsageBudget/AddEmbeddings register in
    // Core (plain AddSingleton wins over their TryAdd regardless of call order). Each needs the SQLite
    // connection factory + schema from UseSqliteStorage, so call that first.

    /// <summary>Back the response cache (<c>AddResponseCache</c>) with SQLite so it survives restarts.
    /// Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, bool, bool)"/> for the factory + schema.</summary>
    public static LyntaiBuilder UseSqliteResponseCache(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<Lyntai.Llm.Caching.IResponseCache>(sp => new SqliteResponseCache(
            sp.GetRequiredService<IDbConnectionFactory>(), sp.GetRequiredService<LyntaiOptions>()));
        return builder;
    }

    /// <summary>Back usage accounting (<c>AddUsageBudget</c>) with SQLite so spend isn't reset every restart.
    /// Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, bool, bool)"/> for the factory + schema.</summary>
    public static LyntaiBuilder UseSqliteUsageTracking(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<Lyntai.Llm.Budgeting.IUsageTracker>(sp => new SqliteUsageTracker(
            sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }

    /// <summary>Back semantic-memory vectors (<c>AddEmbeddings</c>) with SQLite so they survive restarts.
    /// Requires <see cref="UseSqliteStorage(LyntaiBuilder, string, bool, bool)"/> for the factory + schema.</summary>
    public static LyntaiBuilder UseSqliteVectorStore(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<Lyntai.Memory.IVectorStore>(sp => new SqliteVectorStore(
            sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }
}
