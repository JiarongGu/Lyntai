using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>Spins up one PostgreSQL container for the whole Postgres test collection, migrates it
/// once, and exposes a connection factory. If Docker isn't available the fixture reports
/// <see cref="Available"/> = false and every test early-returns (skips) — so the suite stays green on
/// a box without Docker (CI, a fresh checkout) while running for real where Docker is up.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool Available { get; private set; }
    public string? InitError { get; private set; }
    public string ConnectionString { get; private set; } = "";
    public IDbConnectionFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // The catch covers CONTAINER startup only, on purpose. It used to wrap MigrateUp too, which folded
        // a genuinely broken Postgres migration into the Docker-down skip: every Postgres test SKIPPED
        // instead of failing, the skip count is the same in both cases, and a Postgres-only migration
        // (M202608152310 has no SQLite counterpart) could ship broken behind a green run. Environment
        // unavailability skips; a defect in OUR code throws, and xunit fails the whole collection with it.
        try
        {
            // pgvector image (a superset of postgres:16) so the PostgresVectorStore's lazy
            // `CREATE EXTENSION vector` works; every other Postgres test runs against it unchanged.
            _container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
            await _container.StartAsync();
            ConnectionString = _container.GetConnectionString();
        }
        catch (Exception ex)
        {
            Available = false; // Docker unavailable / image pull failed → tests skip
            InitError = $"{ex.GetType().Name}: {ex.Message}";
            return;
        }

        MigrationRunnerService.MigrateUp(ConnectionString);
        Factory = new PostgresConnectionFactory(ConnectionString);
        Available = true;
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
