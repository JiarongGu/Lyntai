using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Xunit;
using Xunit.Abstractions;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// <b>What a recall BUDGET costs a temporal answer</b> — the question every fact in
/// <see cref="TemporalResolutionTests"/> is silent about, because that resolver reads the whole scope.
///
/// <para><b>Why the whole-scope read is not a shippable design.</b> Nothing indexes
/// <c>lyntai.canonical_key</c> on any backend, so there is no narrower query than "give me everything in
/// this scope and let me filter". A real deployment reads a BOUNDED candidate set, which means the correct
/// version has to survive the store's own ordering to be resolvable at all — and design §5.7.0 already
/// records that every miss this subsystem has is "a reachable candidate ranked below the limit, never an
/// unreachable one". This measures whether temporal resolution inherits that.</para>
///
/// <para><b>It REFUTED the hypothesis it was written to check</b>, and the surviving finding is the
/// failure MODE rather than the rate: under budget, a CURRENT probe is never empty and returns a superseded
/// version presented as current — a stale answer <c>docs/DECISIONS.md</c> <b>D90</b> forbids, and one
/// indistinguishable from a correct one at the call site. A historical probe fails loudly instead. The
/// argument is at the assertion that pins it.</para>
///
/// <para><b>A MEASUREMENT, asserted only where the shape is structural.</b> The numbers belong to this
/// corpus and this ordering; the table prints unconditionally, because a measurement that reports only on
/// failure is useless as one.</para>
/// </summary>
/// <param name="output">Where the table goes.</param>
public class BoundedTemporalRecallTests(ITestOutputHelper output)
{
    private const string Engine = "bounded";

    private sealed record Cell(int Budget, string Probe, int Correct, int Stale, int Empty)
    {
        public int Total => Correct + Stale + Empty;
    }

    [Fact]
    public async Task A_budget_below_the_version_count_makes_a_CURRENT_answer_SILENTLY_stale()
    {
        int[] budgets = [1, 2, 3, 5, 10, int.MaxValue];
        var cells = new List<Cell>();

        foreach (var budget in budgets)
        {
            var byProbe = new Dictionary<string, (int Correct, int Stale, int Empty)>(StringComparer.Ordinal)
            {
                ["historical"] = default,
                ["current"] = default,
            };

            for (var seed = 9000; seed < 9012; seed++)
            {
                var corpus = TemporalCorpus.Generate(seed);
                var store = new InMemoryMemoryGraphStore();
                var engine = new GraphMemoryEngine(Engine, store, agePolicies: [new PerWriteAgePolicy()]);
                foreach (var claim in corpus.Claims)
                    await engine.RememberAsync(TemporalCorpus.ToWrite(claim));

                foreach (var key in corpus.Keys)
                {
                    // "current" is the end of the timeline; "historical" is every instant inside it. The
                    // split is the point: they are the same query mechanism asked about different instants.
                    var probes = new (string Kind, DateTimeOffset At)[]
                    {
                        ("current", corpus.Epoch.AddYears(5)),
                        ("historical", corpus.Epoch.AddMonths(1).AddDays(15)),
                        ("historical", corpus.Epoch.AddMonths(4).AddDays(15)),
                        ("historical", corpus.Epoch.AddMonths(7).AddDays(15)),
                    };

                    foreach (var (kind, at) in probes)
                    {
                        var expected = TemporalCorpus.ExpectedAt(corpus, key, at);
                        if (expected.Count == 0) continue;   // nothing in force: not a retrieval question

                        // THE BOUNDED READ a deployment would actually make: a lexical query for the slot,
                        // capped. Every version of a key shares that token, so they compete for the budget.
                        var candidates = await store.SeedAsync(Engine, "t", "s", key, budget);
                        var actual = AssertionResolver
                            .CurrentOf(AssertionResolver.Resolve(candidates, key, at))
                            .Select(a => a.Content).Order(StringComparer.Ordinal).ToList();

                        var slot = byProbe[kind];
                        byProbe[kind] = actual.SequenceEqual(expected) ? slot with { Correct = slot.Correct + 1 }
                            : actual.Count == 0 ? slot with { Empty = slot.Empty + 1 }
                            : slot with { Stale = slot.Stale + 1 };
                    }
                }
            }

            foreach (var (kind, v) in byProbe)
                cells.Add(new Cell(budget, kind, v.Correct, v.Stale, v.Empty));
        }

        Report(cells);

        var unbounded = cells.Where(c => c.Budget == int.MaxValue).ToList();
        var tight = cells.Where(c => c.Budget == 1).ToList();
        var currentAt1 = tight.Single(c => c.Probe == "current");
        var historyAt1 = tight.Single(c => c.Probe == "historical");

        // (1) THE CONTROL. Unbounded is perfect on both probe kinds, or the RESOLVER is what is broken and
        //     every number above it is measuring the wrong thing.
        Assert.All(unbounded, c => Assert.Equal(c.Total, c.Correct));

        // (2) A budget below the versions competing for it breaks resolution. Both probe kinds, and it
        //     saturates once the budget can hold a key's whole history.
        Assert.True(currentAt1.Correct < currentAt1.Total && historyAt1.Correct < historyAt1.Total,
            "a budget of 1 lost nothing — the corpus has no multi-version key for it to lose");
        Assert.All(cells.Where(c => c.Budget >= 5), c => Assert.Equal(c.Total, c.Correct));

        // (3) THE FINDING, AND IT REFUTED THE HYPOTHESIS THIS FILE WAS WRITTEN TO CHECK. The guess was that
        //     a limit costs HISTORICAL answers first, because the store orders by recency and a historical
        //     query needs the oldest version. Measured, historical answers score BETTER than current ones
        //     at every tight budget (42% against 35% at 1). What actually matters is the failure MODE:
        //
        //     A CURRENT probe is never EMPTY. Every candidate it retrieves is in force by then, so an
        //     under-budgeted read always returns something -- and what it returns is a superseded version
        //     presented as current. That is a stale answer, which D90 invariant 4 forbids, and it is
        //     indistinguishable from a correct one at the call site.
        //
        //     A HISTORICAL probe fails LOUDLY instead: its candidates are often not yet in force at the
        //     instant asked about, so it returns nothing and the caller can see it got nothing.
        //
        //     So the dangerous direction is the opposite of the one predicted, and "what is true now" --
        //     the query a memory serves most -- is the one that lies.
        Assert.Equal(0, currentAt1.Empty);
        Assert.True(currentAt1.Stale > 0,
            "a current probe under budget returned no stale answer, so the silent-failure claim is unfounded");
        Assert.True(historyAt1.Empty > 0,
            "a historical probe under budget never came back empty, so the two modes are not distinguishable");
    }

    private void Report(IReadOnlyList<Cell> cells)
    {
        output.WriteLine("budget   probe        correct    stale    empty");
        foreach (var cell in cells.OrderBy(c => c.Budget == int.MaxValue ? int.MaxValue : c.Budget)
                     .ThenBy(c => c.Probe, StringComparer.Ordinal))
        {
            var budget = cell.Budget == int.MaxValue ? "all" : cell.Budget.ToString(CultureInfo.InvariantCulture);
            output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{budget,-8} {cell.Probe,-12} {cell.Correct,7} {cell.Stale,8} {cell.Empty,8}   " +
                $"({(double)cell.Correct / cell.Total:P0} correct)"));
        }
    }
}
