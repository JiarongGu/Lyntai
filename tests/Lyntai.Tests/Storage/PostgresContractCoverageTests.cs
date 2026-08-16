using System.Reflection;

using Lyntai.Tests.Memory;

namespace Lyntai.Tests.Storage;

/// <summary>
/// Every storage contract fact runs on POSTGRES too — checked structurally rather than remembered.
/// </summary>
/// <remarks>
/// <para>The gap this closes. <c>MemoryGraphStoreCoverageTests</c> makes graph-store coverage structural by
/// driving all three backends from one <c>[MemberData]</c> source, so a new fact cannot be added to one
/// backend only. Every OTHER storage contract is wired to Postgres by a hand-maintained list of
/// <c>[SkippableFact]</c> delegators — so a fact added to a contract runs automatically on InMemory and
/// SQLite (they derive from a shared base) and silently does not run on Postgres. Nothing reports that, and
/// on a machine with Docker down the whole leg degrades to a SKIP rather than a failure, so the miss is
/// invisible twice over.</para>
///
/// <para>Measured while writing this file: adding
/// <c>ConversationStoreContract.A_cursor_at_the_same_instant_falls_back_to_the_id</c> required a hand edit to
/// <c>PostgresStorageTests</c> that nothing would have demanded. That fact turned out to fail on Postgres
/// under mutation, so the backend it would have skipped is the one it catches defects on.</para>
///
/// <para>This is a SOURCE-TEXT check, deliberately. A delegator's body cannot be read by reflection, and the
/// alternative — restructuring six suites onto member-data theories — is a much larger change than the
/// defect warrants. The check is exact (whole-identifier match) and its failure message names the missing
/// fact, so it costs one line to satisfy honestly.</para>
/// </remarks>
public class PostgresContractCoverageTests
{
    /// <summary>The contracts whose subject is a STORAGE BACKEND, so every backend owes them. Policy and
    /// engine contracts are excluded: they exercise in-process types that no backend implements.</summary>
    private static readonly string[] BackendContracts =
    [
        nameof(ConversationStoreContract), nameof(CuratedMemoryStoreContract), nameof(KeyValueStoreContract),
        nameof(MemoryStoreContract), nameof(PromptVersionStoreContract), nameof(ScoreStoreContract),
        nameof(TraceStoreContract), nameof(VectorStoreContract),
    ];

    /// <summary>Facts deliberately NOT run against Postgres, each with the reason. An entry that stops
    /// matching FAILS, the same discipline every other allowance in this repository carries — an exclusion
    /// nobody can see expiring is how a gap becomes permanent.</summary>
    private static readonly Dictionary<string, string> NotOnPostgres = new(StringComparer.Ordinal)
    {
        ["ScoreStoreContract.Aggregate_is_per_scorer_across_sessions"] =
            "table-wide aggregate over a SHARED container: other tests' rows fall inside the query, so the "
            + "assertion cannot be made deterministic without giving Postgres its own database per test",
        ["ScoreStoreContract.Export_dumps_every_session_scorer_score"] =
            "same shared-container reason as the aggregate above — the export is table-wide too",
    };

    private static string SuiteSource(string file) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Storage", file));

    [Fact]
    public void Every_backend_contract_fact_is_wired_to_the_Postgres_suite()
    {
        var sources = string.Join("\n",
            SuiteSource("PostgresStorageTests.cs"), SuiteSource("PostgresGovernanceStoreTests.cs"));
        Assert.Contains("SkippableFact", sources, StringComparison.Ordinal); // read the right files at all

        var missing = new List<string>();
        var covered = 0;

        foreach (var contractName in BackendContracts)
        {
            var contract = typeof(PostgresContractCoverageTests).Assembly
                .GetTypes().Single(t => t.Name == contractName);

            var facts = contract.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
                .Select(m => m.Name)
                .ToList();

            Assert.NotEmpty(facts); // a contract that offered nothing would make this vacuously green

            foreach (var fact in facts)
            {
                var qualified = $"{contractName}.{fact}";
                if (NotOnPostgres.ContainsKey(qualified)) continue;
                if (sources.Contains($"{contractName}.{fact}", StringComparison.Ordinal)) covered++;
                else missing.Add(qualified);
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} contract fact(s) never run against Postgres — add a delegator to "
            + "PostgresStorageTests or PostgresGovernanceStoreTests, or record the exclusion with its reason "
            + $"in {nameof(NotOnPostgres)}:\n  " + string.Join("\n  ", missing));
        Assert.True(covered > 0);
    }

    [Fact]
    public void No_exclusion_names_a_fact_that_no_longer_exists()
    {
        // The other half: an allowance that stops matching FAILS, so the list cannot rot into a set of
        // permanent holes nobody re-examines.
        var declared = BackendContracts
            .Select(name => typeof(PostgresContractCoverageTests).Assembly.GetTypes().Single(t => t.Name == name))
            .SelectMany(c => c.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => typeof(Task).IsAssignableFrom(m.ReturnType))
                .Select(m => $"{c.Name}.{m.Name}"))
            .ToHashSet(StringComparer.Ordinal);

        var stale = NotOnPostgres.Keys.Where(k => !declared.Contains(k)).ToList();

        Assert.True(stale.Count == 0,
            "these exclusions name a contract fact that no longer exists — delete them:\n  "
            + string.Join("\n  ", stale));
    }
}
