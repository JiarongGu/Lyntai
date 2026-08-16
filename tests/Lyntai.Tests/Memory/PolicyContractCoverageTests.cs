using System.Reflection;

using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;

namespace Lyntai.Tests.Memory;

/// <summary>
/// Every SHIPPED policy implementation is held to its seam's contract — checked structurally.
/// </summary>
/// <remarks>
/// <para>The gap this closes. <c>MemoryGraphStoreCoverageTests</c> already asserts that every concrete
/// <c>IMemoryGraphStore</c> a Lyntai package ships has a suite driving the whole contract, because a
/// backend with no suite is invisible: nothing fails, and the tick it prints looks identical. The POLICY
/// seams had no equivalent. Coverage was complete on the day this was written — all four age policies, all
/// three ranking policies and both retrievability policies run their contract — and nothing kept it that
/// way. A fifth implementation could ship with no suite and every gate would stay green.</para>
///
/// <para>That is not speculative here. The same shape has been found REAL four times in this repository:
/// a vector-store tiebreak proved on the one backend that already had it, two curated-store facts that
/// never ran against Postgres, and two more found the moment a coverage check was pointed at them. A
/// contract is only worth what runs it.</para>
///
/// <para>SOURCE-TEXT, like its Postgres counterpart, and for the same reason: reflection cannot see which
/// type a test method constructs. The rule is that a shipped implementation must be NAMED in a file that
/// also references its seam's contract — naming it anywhere else (a wiring test, a fake) does not count,
/// which is what makes this about the CONTRACT rather than about being mentioned.</para>
/// </remarks>
public class PolicyContractCoverageTests
{
    /// <summary>Each seam, with the contract class a suite must reference to count as covering it.</summary>
    public static TheoryData<string, string> Seams() => new()
    {
        { nameof(IMemoryAgePolicy), nameof(MemoryAgePolicyContract) },
        { nameof(IMemoryRankingPolicy), nameof(MemoryRankingPolicyContract) },
        { nameof(IMemoryRetrievabilityPolicy), nameof(RetrievabilityPolicyContract) },
    };

    private static readonly string TestDir =
        Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Memory"));

    [Theory]
    [MemberData(nameof(Seams))]
    public void Every_shipped_implementation_of_a_policy_seam_is_run_through_its_contract(
        string seamName, string contractName)
    {
        var seam = typeof(IMemoryAgePolicy).Assembly.GetTypes().Single(t => t.Name == seamName);

        var shipped = typeof(IMemoryAgePolicy).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false, IsPublic: true } && seam.IsAssignableFrom(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(shipped);   // a seam with no implementations would make this vacuously green

        // Only files that exercise the CONTRACT count — being named in a wiring test proves nothing about
        // whether the implementation satisfies the seam's own promises.
        var covering = Directory.EnumerateFiles(TestDir, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Where(text => text.Contains($"{contractName}.", StringComparison.Ordinal)
                        || text.Contains($": {contractName}Facts", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(covering);  // …and so would a contract nothing runs

        // CONSTRUCTION, not mention. A mention is satisfied by the suite's own class NAME —
        // `ContentSizeAgePolicyContractTests` contains `ContentSizeAgePolicy` — so a suite that was renamed
        // to cover one policy while still constructing another would pass. Found by mutation-testing this
        // very check: pointing that suite's factory at a different policy left it green.
        var missing = shipped
            .Where(name => !covering.Any(text => text.Contains($"new {name}(", StringComparison.Ordinal)))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"{missing.Length} shipped {seamName} implementation(s) are never run through {contractName} — "
            + "add a suite, or the seam's promises are unproven for them:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void The_seam_list_itself_names_a_real_contract_for_each_entry()
    {
        // The half the theory cannot check: a typo in a contract name would make `covering` empty and fail
        // loudly, but a seam DROPPED from the list above fails nothing at all — the same one-level-up hole
        // MemoryGraphStoreCoverageTests closes with its own shipped-backend check.
        var declared = Seams().Select(row => (string)row[1]!).ToArray();
        foreach (var contract in declared)
            Assert.True(
                typeof(PolicyContractCoverageTests).Assembly.GetTypes().Any(t => t.Name == contract),
                $"{contract} is named in {nameof(Seams)} but no such contract class exists");

        // And every *Contract class in the memory test namespace is either a POLICY seam listed here or a
        // store contract covered elsewhere — so adding a seam's contract without listing it is visible.
        var policyContracts = typeof(PolicyContractCoverageTests).Assembly.GetTypes()
            .Where(t => t.Name.EndsWith("PolicyContract", StringComparison.Ordinal)
                     || t.Name == nameof(RetrievabilityPolicyContract))
            .Select(t => t.Name)
            .ToArray();

        Assert.Equal(
            policyContracts.OrderBy(n => n, StringComparer.Ordinal),
            declared.OrderBy(n => n, StringComparer.Ordinal));
    }
}
