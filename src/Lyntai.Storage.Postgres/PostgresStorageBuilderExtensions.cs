using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `UsePostgresStorage` shows up right on the builder.
namespace Lyntai;

public static class PostgresStorageBuilderExtensions
{
    /// <summary>Wire every storage domain to PostgreSQL. By default the migrations run now; set
    /// <paramref name="migrateOnFirstUse"/> to defer them to the first store access so DI composition
    /// does no I/O. Every object is <c>lyntai_</c>-prefixed, so the connection may target an existing
    /// application database.</summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, string connectionString, bool migrateOnFirstUse = false)
    {
        if (migrateOnFirstUse)
        {
            builder.Services.AddSingleton<IDbConnectionFactory>(new MigratingConnectionFactory(connectionString));
        }
        else
        {
            MigrationRunnerService.MigrateUp(connectionString);
            builder.Services.AddSingleton<IDbConnectionFactory>(new PostgresConnectionFactory(connectionString));
        }

        builder.Services.AddSingleton<IKeyValueStore, PostgresKeyValueStore>();
        builder.Services.AddSingleton<IPromptVersionStore, PostgresPromptVersionStore>();
        builder.Services.AddSingleton<IConversationStore, PostgresConversationStore>();
        builder.Services.AddSingleton<IMemoryStore>(sp => new PostgresMemoryStore(
            sp.GetRequiredService<IDbConnectionFactory>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<PostgresMemoryStore>>()));
        builder.Services.AddSingleton<IScoreStore, PostgresScoreStore>();
        builder.Services.AddSingleton<ITraceStore, PostgresTraceStore>();
        return builder;
    }
}
