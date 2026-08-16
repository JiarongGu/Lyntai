using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Lyntai.Storage.Sqlite.Migrations;

namespace Lyntai.Tests.Storage;

/// <summary>The awaitable migration entry points (<c>MigrateUpAsync</c>) an app owning its schema under
/// <see cref="SchemaMigration.None"/> calls from an async startup path. FluentMigrator's runner is
/// SYNCHRONOUS, so these promise exactly two things and the tests pin both: the same schema the sync twin
/// lands, and a <see cref="CancellationToken"/> honoured BEFORE any work starts (and between feature
/// passes). A started pass runs to completion — nothing here pretends otherwise.</summary>
public sealed class AsyncMigrationTests : IDisposable
{
    private readonly TempDbPath _db = new("async-migrate"); // fresh, un-migrated — this test owns the schema
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task MigrateUpAsync_lands_the_same_schema_as_the_sync_twin()
    {
        await MigrationRunnerService.MigrateUpAsync(_db.Path);

        using var conn = new SqliteConnectionFactory(_db.Path).Open();
        var versions = conn.Query<long>("SELECT Version FROM lyntai_version_info ORDER BY Version").ToList();
        Assert.Equal(
            [202607280001, 202607280002, 202607280003, 202607280004, 202607280005, 202607280006, 202607280007, 202607280008, 202607280009, 202608081215, 202608121100, 202608161159],
            versions);
    }

    [Fact]
    public async Task MigrateUpAsync_is_idempotent()
    {
        await MigrationRunnerService.MigrateUpAsync(_db.Path);
        await MigrationRunnerService.MigrateUpAsync(_db.Path);

        using var conn = new SqliteConnectionFactory(_db.Path).Open();
        Assert.Equal(12L, conn.ExecuteScalar<long>("SELECT COUNT(*) FROM lyntai_version_info")); // 9 baseline (1.0 squash) + MemoryGraph (2.5.0) + MemoryRetentionModel (3.0 squash) + JobSlots (3.0)
    }

    [Fact]
    public async Task MigrateUpAsync_honours_the_feature_selection()
    {
        await MigrationRunnerService.MigrateUpAsync(_db.Path, StorageFeature.Score);

        Assert.True(TableExists("lyntai_score_result"));   // Score selected → its table lands
        Assert.False(TableExists("lyntai_kv"));            // KeyValue NOT selected → no table
        Assert.False(TableExists("lyntai_memory_entry"));
    }

    /// <summary>The one cancellation promise that is unconditionally true: a token already cancelled when
    /// the call is made stops it before ANY work — not even the database file is created. (Mid-migration
    /// cancellation is impossible: FluentMigrator's runner takes no token.)</summary>
    [Fact]
    public async Task A_token_cancelled_before_the_call_touches_nothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MigrationRunnerService.MigrateUpAsync(_db.Path, cts.Token));

        Assert.False(File.Exists(_db.Path)); // no file, no pragma seed, no version table
    }

    /// <summary>Same promise on the feature-selective overload — a cancelled token cannot be swallowed by
    /// the multi-pass loop.</summary>
    [Fact]
    public async Task A_cancelled_token_stops_the_selective_overload_too()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => MigrationRunnerService.MigrateUpAsync(_db.Path, StorageFeature.Score, cts.Token));

        Assert.False(File.Exists(_db.Path));
    }

    /// <summary>The async twin runs the migration on the CALLING thread — it deliberately does NOT offload
    /// to the thread pool. A <c>Task.Run</c> wrapper would burn a pool thread for the whole schema migration
    /// AND still be uncancellable, so it would be worse than the sync call it wraps. Pinned by the returned
    /// task already being complete when the method returns: nothing was handed to another thread.</summary>
    [Fact]
    public async Task MigrateUpAsync_does_not_offload_the_migration_to_the_thread_pool()
    {
        var task = MigrationRunnerService.MigrateUpAsync(_db.Path);
        var completedBeforeAwaiting = task.IsCompleted;
        await task;

        Assert.True(completedBeforeAwaiting, "MigrateUpAsync must run inline, not on a pool thread");
        Assert.True(TableExists("lyntai_kv")); // and it really did the work
    }

    /// <summary>The Postgres twin makes the SAME promise, and honours it without a server: a cancelled
    /// token stops the call before it ever dials the connection string (so this needs no Docker).</summary>
    [Fact]
    public async Task The_postgres_twin_honours_a_cancelled_token_before_connecting()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Lyntai.Storage.Postgres.Migrations.MigrationRunnerService.MigrateUpAsync(
                "Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope;Timeout=1", cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Lyntai.Storage.Postgres.Migrations.MigrationRunnerService.MigrateUpAsync(
                "Host=127.0.0.1;Port=1;Database=nope;Username=nope;Password=nope;Timeout=1",
                StorageFeature.Score, cts.Token));
    }

    private bool TableExists(string table)
    {
        using var conn = new SqliteConnectionFactory(_db.Path).Open();
        return conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table",
            new { table }) > 0;
    }
}
