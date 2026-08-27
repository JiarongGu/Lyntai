using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Tests.Memory;

/// <summary>
/// The planned "gist" tier needs to report how much SUPPORT a generalisation over recurring entries has.
/// Two candidate rules compete - raw = count(members) vs. weighted = sum(retrievability(member)) - and
/// <see cref="CorpusShape.RoutineCount"/> exists specifically to separate them: phase A (8 members, OLDER)
/// vs. phase B (4 members, NEWER), with the corpus itself declaring phase B correct.
/// <para><b>Whether the rules disagree depends on WRITE PACING</b>, stated as an explicit fixture
/// assumption per arm rather than left implicit. Both drive the SHIPPED <see cref="BurstDampenedAgePolicy"/>:
/// a REAL wall clock folds this ~300-write replay into one 5-second burst, measured at
/// weightedA=6.474743 (range [0.729565,0.923434]) vs weightedB=3.974655 (range [0.987381,1.0]) - raw AND
/// weighted both select phase A, the WRONG regime. An explicit clock stepped 10s/write (over the burst
/// window) degenerates the same policy to per-write ticks, measured at weightedA=1.301059 (range
/// [0.156079,0.173555]) vs weightedB=3.640018 (range [0.830455,1.0]) - raw still selects A, weighted now
/// selects B (correct): a genuine disagreement. rawA=8, rawB=4 in both arms; every weighted figure above is
/// read the instant BEFORE the corpus's final query, which would otherwise reinforce all of phase B to a
/// degenerate retrievability of exactly 1 and hide the real spread.</para>
/// <para><b>Phase B outranks phase A per-member in BOTH arms</b> - the SUM only flips to B once
/// mean(rB)/mean(rA) clears the corpus's fixed cardinality ratio |A|/|B|=2 (stepped 5.60, burst
/// only 1.23): burst damping compresses the range rather than making the rule wrong.</para>
/// <para><b>Which members get a connection boost is itself a function of PACING.</b> Stepped's
/// phase-A spread comes from <c>ConnectionBoost</c> alone (clique A5/A6/A7) - the same
/// ranker-fed-back-into-storage class <c>DsrOptions.ReinforceGain = 0</c> (D54) removed from
/// stability directly. Burst's spread is dominated by write-time ENCODING instead (stability
/// handicapped ~17x by burst position) with a DIFFERENT clique, A0/A1/A2 - see the task report.</para>
/// </summary>
public class MemoryGistSupportRuleTests
{
    private const int Seed = 12345;
    private const int RecallLimit = 10;

    /// <summary>Both regimes' per-member retrievability at one point in the replay.</summary>
    private sealed record RegimeSnapshot(IReadOnlyList<double> PhaseA, IReadOnlyList<double> PhaseB)
    {
        public int RawA => PhaseA.Count;
        public int RawB => PhaseB.Count;
        public double WeightedA => PhaseA.Sum();
        public double WeightedB => PhaseB.Sum();
    }

    [Fact]
    public async Task Raw_and_weighted_support_may_agree_or_disagree_depending_on_write_pacing()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default with { RoutineCount = 12 }, Seed);
        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;

        // The corpus's own declared answer, restated directly against THIS corpus instance.
        // MemoryCorpusTests.The_final_routine_query_names_phase_B_only_and_never_phase_A pins the same
        // fact more strongly (property-based, over every legal RoutineCount).
        var finalRoutineQuery = corpus.Steps.OfType<CorpusQuery>()
            .Last(q => q.RelevantIds.Any(id => id.StartsWith("routine", StringComparison.Ordinal)));
        Assert.All(finalRoutineQuery.RelevantIds,
            id => Assert.StartsWith("routineB", id, StringComparison.Ordinal));

        // ARM 1 - the SHIPPED default policy, driven by a REAL wall clock: GraphMemoryEngine's own
        // default age policy is BurstDampenedAgePolicy, and this replay's ~300 writes complete in well
        // under its own 5-second burst window, so everything after the first write folds into one burst.
        var burst = await RunArmAsync(corpus, firstWrite, finalRoutineQuery, new BurstDampenedAgePolicy());

        // ARM 2 - the SAME shipped policy, driven by an EXPLICIT clock stepped 10 seconds per write, so
        // every write starts its own burst and the damping degenerates to its own inner per-write policy.
        // This states the pacing assumption directly in the fixture instead of silently substituting a
        // different policy.
        var steppedNow = DateTimeOffset.UnixEpoch;
        var stepped = await RunArmAsync(corpus, firstWrite, finalRoutineQuery,
            new BurstDampenedAgePolicy(clock: () => steppedNow),
            onWrite: () => steppedNow += TimeSpan.FromSeconds(10));

        // Both arms: the enumeration taken AFTER the full replay (including the final query) is
        // DEGENERATE for weightedB - the final query recalls and thereby reinforces all four phase-B
        // members, pinning every one at Age <= 0 -> retrievability exactly 1, so weightedB there carries
        // no information rawB did not already have. Documented directly rather than only in prose.
        Assert.Equal(burst.AfterFullReplay.RawB, burst.AfterFullReplay.WeightedB, precision: 6);
        Assert.Equal(stepped.AfterFullReplay.RawB, stepped.AfterFullReplay.WeightedB, precision: 6);

        // The BEFORE-final-query snapshot is the robust figure and the one both verdicts below are based
        // on - both sums, both counts and both ranges sit in the assertion messages so a regression prints
        // the measurement, not just PASS/FAIL.
        var b = burst.BeforeFinalQuery;
        var s = stepped.BeforeFinalQuery;

        // Under the literal shipped default (real clock): raw AND weighted both select phase A, which the
        // corpus declares WRONG. They AGREE, on the wrong answer - recorded as the finding it is.
        Assert.True(b.RawA > b.RawB, $"burst raw: A={b.RawA} B={b.RawB}");
        Assert.True(b.WeightedA > b.WeightedB,
            $"burst weighted: A={b.WeightedA:F6} (n={b.RawA}, range={RangeText(b.PhaseA)}) "
            + $"B={b.WeightedB:F6} (n={b.RawB}, range={RangeText(b.PhaseB)})");

        // Under an explicitly spaced clock (>= 5s between writes, stated in the fixture): raw still
        // selects phase A, but weighted now selects phase B, the corpus's correct answer. Disagreement.
        Assert.True(s.RawA > s.RawB, $"stepped raw: A={s.RawA} B={s.RawB}");
        Assert.True(s.WeightedB > s.WeightedA,
            $"stepped weighted: A={s.WeightedA:F6} (n={s.RawA}, range={RangeText(s.PhaseA)}) "
            + $"B={s.WeightedB:F6} (n={s.RawB}, range={RangeText(s.PhaseB)})");
    }

    private static async Task<(RegimeSnapshot BeforeFinalQuery, RegimeSnapshot AfterFullReplay)> RunArmAsync(
        MemoryCorpus corpus, MemoryWrite firstWrite, CorpusQuery finalRoutineQuery,
        IMemoryAgePolicy agePolicy, Action? onWrite = null)
    {
        var store = new InMemoryMemoryGraphStore();
        const string engineName = "gist-support";
        var engine = new GraphMemoryEngine(engineName, store, agePolicies: [agePolicy]);
        RegimeSnapshot? beforeFinalQuery = null;

        // IN TIMELINE ORDER - writes and queries interleaved, exactly as MemoryCorpus's own ordering
        // contract requires. The snapshot for the robust weightedB figure is taken the instant BEFORE the
        // final routine query executes, so it captures real decay without also capturing that query's own
        // reinforcement of what it returns.
        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    onWrite?.Invoke();
                    await engine.RememberAsync(w.Write);
                    break;

                case CorpusQuery q:
                    if (ReferenceEquals(q, finalRoutineQuery))
                        beforeFinalQuery = await SnapshotAsync(store, engineName, firstWrite, corpus);
                    await engine.RecallAsync(
                        new MemoryQuery(firstWrite.TaskKey, firstWrite.Scope, q.Text, Limit: RecallLimit));
                    break;

                case CorpusExpand:
                    break; // expansions are off on this shape (ExpandRatio = 0)
            }
        }

        var afterFullReplay = await SnapshotAsync(store, engineName, firstWrite, corpus);
        return (beforeFinalQuery!, afterFullReplay);
    }

    /// <summary>Reads the current stored state with a null-query <see cref="IMemoryGraphStore.SeedAsync"/>
    /// - the enumeration path, which does NOT reinforce what it returns - and buckets every routine member
    /// into its regime by <see cref="MemoryCorpusTestAccess.IdOf"/>.</summary>
    private static async Task<RegimeSnapshot> SnapshotAsync(InMemoryMemoryGraphStore store, string engineName,
        MemoryWrite firstWrite, MemoryCorpus corpus)
    {
        var writeCount = corpus.Steps.OfType<CorpusWrite>().Count();
        var nodes = await store.SeedAsync(engineName, firstWrite.TaskKey, firstWrite.Scope,
            query: null, limit: writeCount + 10);

        var retrievability = new DsrRetrievability();
        var phaseA = new List<double>();
        var phaseB = new List<double>();
        foreach (var node in nodes)
        {
            var id = MemoryCorpusTestAccess.IdOf(node.Content);
            if (id.StartsWith("routineA", StringComparison.Ordinal))
                phaseA.Add(retrievability.Retrievability(node.DecayState));
            else if (id.StartsWith("routineB", StringComparison.Ordinal))
                phaseB.Add(retrievability.Retrievability(node.DecayState));
        }
        return new RegimeSnapshot(phaseA, phaseB);
    }

    // Guards against Assert.True's eager string-interpolation evaluating Min()/Max() on an empty bucket
    // (InvalidOperationException) before the assertion itself ever runs.
    private static string RangeText(IReadOnlyList<double> values) =>
        values.Count == 0 ? "empty" : $"[{values.Min():F6},{values.Max():F6}]";
}
