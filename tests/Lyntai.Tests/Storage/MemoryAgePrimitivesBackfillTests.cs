using Dapper;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Initialization;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage;

/// <summary>
/// I1 (fix round 1): every OTHER migration test runs on a fresh database, where
/// <c>M202608100900_MemoryAgePrimitives</c>'s two <c>UPDATE ... FROM</c> statements affect ZERO rows — the
/// suite proved the columns exist and proved nothing about their VALUES. This pauses the migration run
/// mid-history, inserts legacy rows whose id-INSERTION order deliberately disagrees with their
/// <c>created_at</c> order, then finishes migrating and asserts the backfilled values — so a regression that
/// dropped the <c>id</c> tiebreaker, swapped the <c>ORDER BY</c> for insertion order, or broke the
/// <c>PARTITION BY engine</c> would fail this, not just leave the columns silently wrong.
/// </summary>
public sealed class MemoryAgePrimitivesBackfillTests : IDisposable
{
    // everything up to and including MemorySignals — the schema this migration's Up() actually reads from
    private const long BeforeMemoryAgePrimitives = 202608090822;
    private readonly TempDbPath _path = new("backfill");

    public void Dispose() => _path.Dispose();

    [Fact]
    public void Backfill_derives_ordinals_and_running_chars_from_created_at_order_not_insertion_order()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = _path.Path }.ToString();
        SeedPragmas(connectionString);
        RunTo(connectionString, BeforeMemoryAgePrimitives);

        using (var conn = new SqliteConnection(connectionString))
        {
            conn.Open();

            // engine "e1": id-INSERTION order (1, 2, 3) deliberately disagrees with created_at order
            // (2, 3, 1) — if the backfill used id/insertion order instead of created_at, this catches it.
            // A lyntai_memory_position ROW is what a real UpsertAsync would already have created alongside
            // these nodes (the store always writes both in one transaction) — its own `position` value is
            // irrelevant to this fact, only `ordinal`/`chars`/`encoded_at`, which the migration overwrites.
            conn.Execute("INSERT INTO lyntai_memory_position (engine, position) VALUES ('e1', 3)");
            InsertLegacyNode(conn, "e1", createdAt: "2026-01-03T00:00:00Z", content: new string('a', 5)); // id 1
            InsertLegacyNode(conn, "e1", createdAt: "2026-01-01T00:00:00Z", content: new string('b', 3)); // id 2
            InsertLegacyNode(conn, "e1", createdAt: "2026-01-02T00:00:00Z", content: new string('c', 7)); // id 3

            // engine "e2": proves PARTITION BY engine — its own ordinal must start at 1, unaffected by e1's
            // three rows already written
            conn.Execute("INSERT INTO lyntai_memory_position (engine, position) VALUES ('e2', 2)");
            InsertLegacyNode(conn, "e2", createdAt: "2026-02-01T00:00:00Z", content: new string('d', 2)); // id 4
            InsertLegacyNode(conn, "e2", createdAt: "2026-02-02T00:00:00Z", content: new string('e', 3)); // id 5

            // engine "empty": written, then every row deleted BEFORE migrating — its lyntai_memory_position
            // row survives (nothing deletes it), but nothing is left for MAX(encoding_*) to find
            InsertLegacyNode(conn, "empty", createdAt: "2026-03-01T00:00:00Z", content: "x"); // id 6
            InsertLegacyNode(conn, "empty", createdAt: "2026-03-02T00:00:00Z", content: "y"); // id 7
            conn.Execute("INSERT INTO lyntai_memory_position (engine, position) VALUES ('empty', 2)");
            conn.Execute("DELETE FROM lyntai_memory_node WHERE engine = 'empty'");
        }

        RunTo(connectionString, null); // finish the migration set, including MemoryAgePrimitives

        using var check = new SqliteConnection(connectionString);
        check.Open();

        var e1 = check.Query("""
            SELECT id, encoding_ordinal AS EncodingOrdinal, encoding_chars AS EncodingChars
            FROM lyntai_memory_node WHERE engine = 'e1' ORDER BY id
            """).ToDictionary(r => (long)r.id, r => r);

        // id 1 was created LAST (2026-01-03) despite being inserted FIRST: ordinal 3, cumulative 3+7+5=15 —
        // NOT ordinal 1, which is what id/insertion order would wrongly give it
        Assert.Equal(3L, (long)e1[1].EncodingOrdinal);
        Assert.Equal(15L, (long)e1[1].EncodingChars);
        // id 2 was created FIRST (2026-01-01) despite being inserted SECOND: ordinal 1, cumulative 3 (its
        // own content length alone)
        Assert.Equal(1L, (long)e1[2].EncodingOrdinal);
        Assert.Equal(3L, (long)e1[2].EncodingChars);
        // id 3 was created MIDDLE (2026-01-02): ordinal 2, cumulative 3+7=10
        Assert.Equal(2L, (long)e1[3].EncodingOrdinal);
        Assert.Equal(10L, (long)e1[3].EncodingChars);

        var e2 = check.Query("""
            SELECT id, encoding_ordinal AS EncodingOrdinal, encoding_chars AS EncodingChars
            FROM lyntai_memory_node WHERE engine = 'e2' ORDER BY id
            """).ToDictionary(r => (long)r.id, r => r);
        Assert.Equal(1L, (long)e2[4].EncodingOrdinal); // e2's own count starts at 1, not e1's 4th write
        Assert.Equal(2L, (long)e2[4].EncodingChars);
        Assert.Equal(2L, (long)e2[5].EncodingOrdinal);
        Assert.Equal(5L, (long)e2[5].EncodingChars); // 2 + 3

        var positions = check.Query("SELECT engine, ordinal, chars FROM lyntai_memory_position ORDER BY engine")
            .ToDictionary(r => (string)r.engine, r => r);
        Assert.Equal(3L, (long)positions["e1"].ordinal);
        Assert.Equal(15L, (long)positions["e1"].chars);
        Assert.Equal(2L, (long)positions["e2"].ordinal);
        Assert.Equal(5L, (long)positions["e2"].chars);
        // "empty": a position row exists but zero nodes survive to migration time — the backfill's MAX()
        // subquery finds no matching engine, so the UPDATE...FROM never touches this row and it keeps the
        // ADD COLUMN placeholder default rather than crashing or silently inheriting a stale value
        Assert.Equal(0L, (long)positions["empty"].ordinal);
        Assert.Equal(0L, (long)positions["empty"].chars);
    }

    private static void InsertLegacyNode(SqliteConnection conn, string engine, string createdAt, string content) =>
        conn.Execute("""
            INSERT INTO lyntai_memory_node
                (engine, task_key, scope, headline, content, content_hash, grade,
                 created_at, last_recalled_position, recall_count, stability)
            VALUES (@engine, 't', 's', @content, @content, @content, 1, @createdAt, 0, 0, 7)
            """, new { engine, content, createdAt });

    private static void SeedPragmas(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA busy_timeout=5000; PRAGMA foreign_keys=ON;";
        cmd.ExecuteNonQuery();
    }

    /// <summary>Runs the <see cref="StorageFeatures.AllTag"/> pass up to and including
    /// <paramref name="version"/>, or every remaining migration when null — mirrors
    /// <c>MigrationRunnerService.RunPass</c> exactly, except it drives FluentMigrator's own
    /// version-targeted <c>MigrateUp</c> so this test can pause mid-history and insert legacy data.</summary>
    private static void RunTo(string connectionString, long? version)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => rb
                .AddSQLite()
                .WithGlobalConnectionString(connectionString)
                .ScanIn(typeof(MigrationRunnerService).Assembly).For.All());
        services.Configure<RunnerOptions>(opt => opt.Tags = [StorageFeatures.AllTag]);

        using var provider = services.BuildServiceProvider(validateScopes: false);
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();
        if (version is long v) runner.MigrateUp(v); else runner.MigrateUp();
    }
}
