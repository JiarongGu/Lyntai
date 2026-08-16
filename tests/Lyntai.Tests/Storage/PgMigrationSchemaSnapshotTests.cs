using System.Text;
using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Postgres.Migrations;
using Testcontainers.PostgreSql;
using Xunit;

namespace Lyntai.Tests.Storage;

/// <summary>The load-bearing gate for the 1.0 Postgres migration-baseline squash — the parallel of
/// <see cref="MigrationSchemaSnapshotTests"/> for the SQLite backend. Postgres stores no raw CREATE text,
/// so this is a NORMALIZED CATALOG snapshot: the column SET (name/type/nullability/default, ordered by
/// <c>table_name, column_name</c> — POSITION-AGNOSTIC, so cosmetic <c>ordinal_position</c> gaps left by
/// historically dropped columns don't matter) + indexes (canonical <c>indexdef</c>, incl. PK/unique)
/// + foreign keys (with ON DELETE), all filtered to <c>lyntai_</c> objects and ordered deterministically.
/// Nothing in Lyntai depends on physical column order (all access is by name via Dapper). The canonical
/// catalog is identical whether a column arrived via CREATE or a later ALTER, so collapsing the accreted
/// migrations into per-domain baselines that are semantically equal must still MATCH the golden captured
/// from the pre-squash set. Runs against a THROWAWAY, migrations-only container — NOT the
/// shared fixture db, whose <c>PostgresVectorStore</c> tests lazily create <c>lyntai_vector</c> (leaking it
/// into the dump under some test orderings). A fresh migrate-only db has no <c>lyntai_vector</c> (pgvector
/// stays lazy) and no <c>lyntai_version_info</c> (excluded), exactly as the golden intends. Skips when
/// Docker is unavailable. Regenerate the golden ONLY on a DELIBERATE schema change:
/// <c>LYNTAI_UPDATE_SCHEMA_SNAPSHOT=1</c>.</summary>
[Collection("postgres")]
public sealed class PgMigrationSchemaSnapshotTests(PostgresFixture pg)
{
    // mirrors MigrationSchemaSnapshotTests.SnapshotDir — the test assembly runs from bin/<cfg>/<tfm>, so
    // ..\..\.. is the project dir; keeps the golden a tracked, reviewable fixture without a compile-time path.
    private static string SnapshotDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Storage", "Snapshots"));

    [SkippableFact]
    public async Task Fresh_migrated_schema_matches_golden()
    {
        Skip.IfNot(pg.Available, pg.InitError ?? "Postgres/Docker unavailable");

        // A dedicated throwaway container migrated with the FULL feature set and NOTHING else — so the dump
        // reflects only what the migrations create (no lazily-created lyntai_vector, no other tests' rows).
        await using var container = new PostgreSqlBuilder("pgvector/pgvector:pg16").Build();
        await container.StartAsync();
        var cs = container.GetConnectionString();
        MigrationRunnerService.MigrateUp(cs);
        var factory = new PostgresConnectionFactory(cs);

        var actual = await DumpSchema(factory);
        Directory.CreateDirectory(SnapshotDir);
        var goldenPath = Path.Combine(SnapshotDir, "pg-schema.txt");

        // Regeneration FAILS the run, for the reason spelled out in MigrationSchemaSnapshotTests' twin: the
        // write preceded the compare, so with this variable set the assertion read actual == actual and could
        // not fail. Both schema guards had that shape, so a stray export disarmed BOTH at once.
        if (Environment.GetEnvironmentVariable("LYNTAI_UPDATE_SCHEMA_SNAPSHOT") == "1")
        {
            File.WriteAllText(goldenPath, actual);
            Assert.Fail($"golden regenerated: {goldenPath} — re-run WITHOUT LYNTAI_UPDATE_SCHEMA_SNAPSHOT " +
                "to verify it, and review the diff before committing it");
        }

        Assert.True(File.Exists(goldenPath),
            $"golden missing: {goldenPath} — capture once with LYNTAI_UPDATE_SCHEMA_SNAPSHOT=1");
        Assert.Equal(Norm(File.ReadAllText(goldenPath)), Norm(actual));
    }

    // A deterministic, normalized dump of every lyntai_ catalog object — the column SET (name/type/
    // nullability/default, ordered by name so physical position is ignored), indexes (canonical indexdef,
    // which covers PK/unique too), and foreign keys with ON DELETE — minus the migrator's own version
    // table. Ordering is fixed for a stable diff; the canonical form is identical whether a column arrived
    // via CREATE or ALTER (and regardless of position), so a clean rewrite that is semantically equal
    // matches byte-for-byte.
    private static async Task<string> DumpSchema(IDbConnectionFactory factory)
    {
        using var conn = factory.Open();
        var sb = new StringBuilder();

        sb.Append("== columns ==\n");
        var columns = await conn.QueryAsync(
            """
            SELECT table_name, column_name, data_type, is_nullable, column_default
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name LIKE 'lyntai\_%' ESCAPE '\'
              AND table_name <> 'lyntai_version_info'
            ORDER BY table_name, column_name
            """);
        foreach (var c in columns)
            sb.Append($"{c.table_name}|{c.column_name}|{c.data_type}|{c.is_nullable}|{c.column_default}\n");

        sb.Append("\n== indexes ==\n");
        var indexes = await conn.QueryAsync(
            """
            SELECT tablename, indexname, indexdef
            FROM pg_indexes
            WHERE schemaname = 'public'
              AND tablename LIKE 'lyntai\_%' ESCAPE '\'
              AND tablename <> 'lyntai_version_info'
            ORDER BY tablename, indexname
            """);
        foreach (var i in indexes)
            sb.Append($"{i.tablename}|{i.indexname}|{i.indexdef}\n");

        sb.Append("\n== foreign_keys ==\n");
        var fks = await conn.QueryAsync(
            """
            SELECT tc.table_name       AS table_name,
                   kcu.column_name     AS column_name,
                   ccu.table_name      AS ref_table,
                   ccu.column_name     AS ref_column,
                   rc.delete_rule      AS delete_rule
            FROM information_schema.table_constraints tc
            JOIN information_schema.key_column_usage kcu
                 ON kcu.constraint_name = tc.constraint_name AND kcu.constraint_schema = tc.constraint_schema
            JOIN information_schema.constraint_column_usage ccu
                 ON ccu.constraint_name = tc.constraint_name AND ccu.constraint_schema = tc.constraint_schema
            JOIN information_schema.referential_constraints rc
                 ON rc.constraint_name = tc.constraint_name AND rc.constraint_schema = tc.constraint_schema
            WHERE tc.constraint_type = 'FOREIGN KEY'
              AND tc.table_schema = 'public'
              AND tc.table_name LIKE 'lyntai\_%' ESCAPE '\'
              AND tc.table_name <> 'lyntai_version_info'
            ORDER BY tc.table_name, kcu.column_name
            """);
        foreach (var f in fks)
            sb.Append($"{f.table_name}|{f.column_name}|{f.ref_table}|{f.ref_column}|{f.delete_rule}\n");

        return sb.ToString();
    }

    private static string Norm(string s) => s.Replace("\r\n", "\n").TrimEnd();
}
