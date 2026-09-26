using System.Text;
using Dapper;

namespace Lyntai.Tests.Storage;

/// <summary>The fresh-db schema gate: a fresh migrate must produce a net <c>lyntai_</c> schema
/// byte-identical to the tracked golden, so no migration change drifts the fresh-db schema silently.
/// Regenerate the golden ONLY on a DELIBERATE schema change: set <c>LYNTAI_UPDATE_SCHEMA_SNAPSHOT=1</c>.</summary>
public class MigrationSchemaSnapshotTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    // mirrors ApiSurfaceTests.BaselineDir — the test assembly runs from bin/<cfg>/<tfm>, so ..\..\.. is the
    // project dir; keeps the golden a tracked, reviewable fixture without baking a compile-time path.
    private static string SnapshotDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Storage", "Snapshots"));

    [Fact]
    public void Fresh_migrated_schema_matches_golden()
    {
        var actual = DumpSchema();
        Directory.CreateDirectory(SnapshotDir);
        var goldenPath = Path.Combine(SnapshotDir, "sqlite-schema.sql");

        // Regeneration FAILS the run on purpose: the write happens before the compare, so a passing run here
        // would read actual == actual. A stray export (a shell profile, a CI variable, a leftover from a
        // deliberate regeneration) would turn both schema guards into no-ops that read as coverage.
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

    // Every lyntai_ table/index/trigger/FTS object (+ its stored DDL), minus the migrator's own version
    // table. SQLite rewrites the stored CREATE text on ALTER/DROP COLUMN, so this reflects the true NET
    // schema; ordering is deterministic for a stable diff.
    private string DumpSchema()
    {
        using var conn = _db.Factory.Open();
        var rows = conn.Query(
            """
            SELECT type, name, sql FROM sqlite_master
            WHERE (name LIKE 'lyntai\_%' ESCAPE '\'
                OR name LIKE 'ix\_lyntai\_%' ESCAPE '\'
                OR name LIKE 'ux\_lyntai\_%' ESCAPE '\')
              AND name <> 'lyntai_version_info' AND sql IS NOT NULL
            ORDER BY type, name
            """);
        var sb = new StringBuilder();
        foreach (var r in rows) sb.Append($"-- {r.type} {r.name}\n{r.sql};\n\n");
        return sb.ToString();
    }

    private static string Norm(string s) => s.Replace("\r\n", "\n").TrimEnd();
}
