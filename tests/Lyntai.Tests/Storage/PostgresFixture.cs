using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>Spins up one PostgreSQL container for the whole Postgres test collection, migrates it
/// once, and exposes a connection factory. If Docker isn't available the fixture reports
/// <see cref="Available"/> = false and every test SKIPS (<c>Skip.IfNot</c>, never an early return, which
/// would count as a pass) — so a box without Docker reports the leg as skipped rather than green.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;

    public bool Available { get; private set; }
    public string? InitError { get; private set; }
    public string ConnectionString { get; private set; } = "";
    public IDbConnectionFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // The catch covers CONTAINER startup only, on purpose. Wrapping MigrateUp too folds a broken
        // Postgres migration into the Docker-down skip: every Postgres test SKIPS instead of failing, at the
        // same skip count, so a Postgres-only migration (M202608152310 has no SQLite counterpart) could ship
        // broken behind a green run. Environment unavailability skips; a defect in OUR code throws, and xunit
        // fails the whole collection with it.
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
            // The prefix is how `verify` tells a Postgres skip from a live-backend one (`_test-evidence.mjs`).
            InitError = $"Postgres/Docker unavailable: {ex.GetType().Name}: {ex.Message}";
            return;
        }

        MigrationRunnerService.MigrateUp(ConnectionString);
        Factory = new PostgresConnectionFactory(ConnectionString);
        Available = true;
    }

    /// <summary>The factory, or a SKIP when the container never started — for a test to call first.</summary>
    public IDbConnectionFactory Require()
    {
        Skip.IfNot(Available, InitError ?? "Postgres/Docker unavailable");
        return Factory;
    }

    /// <summary>Empties <paramref name="tables"/>. Safe only because every Postgres suite shares the
    /// <c>postgres</c> collection and therefore runs serially.</summary>
    public async Task ClearAsync(params string[] tables)
    {
        using var conn = Factory.Open();
        foreach (var table in tables)
            await Dapper.SqlMapper.ExecuteAsync(conn, $"DELETE FROM {table}");
    }

    public async Task DisposeAsync()
    {
        if (_container is not null) await _container.DisposeAsync();
    }
}

[CollectionDefinition("postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>;
