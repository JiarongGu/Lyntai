using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `UseSqliteStorage` shows up right on the builder with no extra using.
namespace Lyntai;

public static class SqliteStorageBuilderExtensions
{
    /// <summary>Wire every storage domain to SQLite at <paramref name="dbPath"/>: runs the migrations
    /// (creating the file/folder as needed) and registers the connection factory + all five stores.</summary>
    public static LyntaiBuilder UseSqliteStorage(this LyntaiBuilder builder, string dbPath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(dbPath));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        MigrationRunnerService.MigrateUp(dbPath);

        builder.Services.AddSingleton<IDbConnectionFactory>(new SqliteConnectionFactory(dbPath));
        builder.Services.AddSingleton<IKeyValueStore, SqliteKeyValueStore>();
        builder.Services.AddSingleton<IConversationStore, SqliteConversationStore>();
        builder.Services.AddSingleton<IMemoryStore>(sp => new SqliteMemoryStore(
            sp.GetRequiredService<IDbConnectionFactory>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<SqliteMemoryStore>>()));
        builder.Services.AddSingleton<IScoreStore, SqliteScoreStore>();
        builder.Services.AddSingleton<ITraceStore, SqliteTraceStore>();
        return builder;
    }
}
