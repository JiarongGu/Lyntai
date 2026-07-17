using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `UseSqliteStorage` shows up right on the builder with no extra using.
namespace Lyntai;

public static class SqliteStorageBuilderExtensions
{
    /// <summary>Wire every storage domain to SQLite at <paramref name="dbPath"/>: registers the
    /// connection factory + all six stores.
    /// <para><paramref name="migrateOnFirstUse"/> defers migrations to the first store access so DI
    /// composition does no I/O (AOT/startup-sensitive hosts, container health checks).</para>
    /// <para><paramref name="migrate"/>=false makes the APP own the schema — Lyntai runs no migrations,
    /// assuming the tables (see the <c>lyntai_*</c> schema) already exist. Run
    /// <see cref="MigrationRunnerService.MigrateUp"/> yourself if you want Lyntai's schema on your own
    /// terms.</para></summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, string dbPath,
        bool migrateOnFirstUse = false, bool migrate = true)
    {
        IDbConnectionFactory factory;
        if (!migrate)
        {
            factory = new SqliteConnectionFactory(dbPath); // app owns the schema — no migrations
        }
        else if (migrateOnFirstUse)
        {
            factory = new MigratingConnectionFactory(dbPath);
        }
        else
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            MigrationRunnerService.MigrateUp(dbPath);
            factory = new SqliteConnectionFactory(dbPath);
        }
        return builder.UseSqliteStorage(factory);
    }

    /// <summary>Wire every storage domain to SQLite using an APP-SUPPLIED <see cref="IDbConnectionFactory"/> —
    /// so the app owns connection creation, pooling, and lifecycle (e.g. a connection drawn from its own
    /// pool). Lyntai runs no migrations here; own the schema, or migrate on your own factory beforehand.
    /// The SQL is SQLite-dialect, so the factory must open SQLite connections.</summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, IDbConnectionFactory factory)
    {
        builder.Services.AddSingleton(factory);
        builder.Services.AddSingleton<IKeyValueStore, SqliteKeyValueStore>();
        builder.Services.AddSingleton<IPromptVersionStore, SqlitePromptVersionStore>();
        builder.Services.AddSingleton<IConversationStore, SqliteConversationStore>();
        builder.Services.AddSingleton<IMemoryStore>(sp => new SqliteMemoryStore(
            sp.GetRequiredService<IDbConnectionFactory>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<SqliteMemoryStore>>()));
        builder.Services.AddSingleton<IScoreStore, SqliteScoreStore>();
        builder.Services.AddSingleton<ITraceStore, SqliteTraceStore>();
        builder.Services.AddSingleton<IJobStore>(sp => new SqliteJobStore(sp.GetRequiredService<IDbConnectionFactory>()));
        return builder;
    }
}
