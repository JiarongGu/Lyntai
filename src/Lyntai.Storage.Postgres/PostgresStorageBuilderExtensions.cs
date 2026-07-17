using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Microsoft.Extensions.DependencyInjection;
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
        bool migrateOnFirstUse = false, bool migrate = true)
    {
        IDbConnectionFactory factory;
        if (!migrate)
        {
            factory = new PostgresConnectionFactory(connectionString); // app owns the schema
        }
        else if (migrateOnFirstUse)
        {
            factory = new MigratingConnectionFactory(connectionString);
        }
        else
        {
            MigrationRunnerService.MigrateUp(connectionString);
            factory = new PostgresConnectionFactory(connectionString);
        }
        return builder.UsePostgresStorage(factory);
    }

    /// <summary>Wire every storage domain to PostgreSQL using an APP-SUPPLIED
    /// <see cref="IDbConnectionFactory"/> — the app owns connection creation, pooling, and lifecycle.
    /// Lyntai runs no migrations here; own the schema, or migrate beforehand. The SQL is Postgres-dialect,
    /// so the factory must open Npgsql connections.</summary>
    public static LyntaiBuilder UsePostgresStorage(this LyntaiBuilder builder, IDbConnectionFactory factory)
    {
        builder.Services.AddSingleton(factory);
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
