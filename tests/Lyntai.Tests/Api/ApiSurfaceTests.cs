using Lyntai.Llm;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Postgres;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Api;

/// <summary>
/// Approval test for the public API surface of every packable Lyntai assembly. A checked-in baseline
/// (<c>ApiSurface/&lt;Assembly&gt;.txt</c>) is the source of truth: any add/remove/rename of public or
/// protected surface fails this test until the baseline is updated deliberately — so pre-1.0 breaks
/// are visible in review and, post-1.0, gate a major bump.
///
/// To update a baseline after an intentional change: delete the file and re-run (it re-seeds), or copy
/// the emitted <c>.actual</c> file over it. Baselines seed automatically on first run.
/// </summary>
public class ApiSurfaceTests
{
    public static TheoryData<string> Assemblies() =>
    [
        "Lyntai.Core",
        "Lyntai.Storage.Sqlite",
        "Lyntai.Storage.InMemory",
        "Lyntai.Storage.Postgres",
        "Lyntai.Providers.ClaudeCli",
        "Lyntai.Providers.OpenAiCompatible",
        "Lyntai.Providers.ExtensionsAi",
    ];

    // anchor a known public type from each assembly so it's loaded + resolvable by simple name
    private static readonly Dictionary<string, System.Reflection.Assembly> Loaded = new()
    {
        ["Lyntai.Core"] = typeof(ILlmProvider).Assembly,
        ["Lyntai.Storage.Sqlite"] = typeof(SqliteConnectionFactory).Assembly,
        ["Lyntai.Storage.InMemory"] = typeof(InMemoryKeyValueStore).Assembly,
        ["Lyntai.Storage.Postgres"] = typeof(PostgresConnectionFactory).Assembly,
        ["Lyntai.Providers.ClaudeCli"] = typeof(Lyntai.Providers.ClaudeCli.ClaudeCliProvider).Assembly,
        ["Lyntai.Providers.OpenAiCompatible"] = typeof(Lyntai.Providers.OpenAiCompatible.OpenAiCompatibleProvider).Assembly,
        ["Lyntai.Providers.ExtensionsAi"] = typeof(Lyntai.Providers.ExtensionsAi.ExtensionsAiProvider).Assembly,
    };

    private static string BaselineDir => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "Api", "Baselines"));

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Public_surface_matches_the_baseline(string assemblyName)
    {
        var actual = ApiSurface.Render(Loaded[assemblyName]);
        Directory.CreateDirectory(BaselineDir);
        var baselinePath = Path.Combine(BaselineDir, $"{assemblyName}.txt");

        if (!File.Exists(baselinePath))
        {
            File.WriteAllText(baselinePath, actual); // seed on first run; commit the result
            return;
        }

        var expected = File.ReadAllText(baselinePath).Replace("\r\n", "\n");
        if (expected != actual)
        {
            File.WriteAllText(baselinePath + ".actual", actual); // for easy diff/update
            Assert.Fail($"Public API of {assemblyName} changed. Review the diff against " +
                $"{baselinePath}; if intentional, replace it with the emitted .actual file (and note the break in CHANGELOG).");
        }
    }
}
