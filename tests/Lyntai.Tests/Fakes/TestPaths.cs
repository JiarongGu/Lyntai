namespace Lyntai.Tests.Fakes;

/// <summary>Repo-relative test scratch locations (family rule: scratch under <c>devtools/_*</c>, never
/// OS temp). Centralizes the <c>AppContext.BaseDirectory + "../../../../.."</c> walk to the repo root
/// that was previously hand-rolled per test file.</summary>
public static class TestPaths
{
    /// <summary>Full path of <c>devtools/&lt;name&gt;</c> under the repo root; the directory is created.</summary>
    public static string DevtoolsDir(string name)
    {
        var dir = Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..", "devtools", name));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary><c>devtools/_test-dbs</c> — per-test SQLite database files (gitignored).</summary>
    public static string TestDbsDir => DevtoolsDir("_test-dbs");

    /// <summary><c>devtools/_test-scratch</c> — miscellaneous per-test scratch files (gitignored).</summary>
    public static string TestScratchDir => DevtoolsDir("_test-scratch");
}
