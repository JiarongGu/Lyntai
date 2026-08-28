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
/// <para><b>Phase B outranks phase A per-member in both arms</b> - ASSERTED below in the strong form, min
/// of B against max of A, rather than described. That is what makes the SUM's verdict a question about
/// CARDINALITY alone: it flips to B only once mean(rB)/mean(rA) clears |A|/|B|, which is NOT a constant -
/// phase B is <c>max(1, RoutineCount/3)</c>, so the ratio is 2 only at multiples of 3 and reaches 4.0 at
/// RoutineCount=5. This shape IS a multiple of 3, the value where RoutineCount=9 once hid the routine
/// split's own defect, so do not generalise a constant off it.</para>
/// <para><b>Scope: ONE shape, ONE seed</b> - <c>ReuseRatio 4</c>, outside the 60-shape grid the routine
/// class's preconditions are proved over, and the co-activation clique differs BETWEEN arms at the same
/// seed. That grid is swept by <c>node devtools/dev.mjs memory-support</c>; what it measured, and what it
/// could not, is <c>docs/memory.md</c> §5.</para>
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

    /// <summary>Raw and weighted support over the same corpus under two write pacings.
    /// <para><b>Both arms state their pacing as an INJECTED clock</b> - no wall clock is read anywhere in
    /// this test, so no figure here is a fact about how fast this machine ran the replay
    /// (<c>.claude/knowledge/pitfalls.md</c> §Testing). Both drive the SHIPPED
    /// <see cref="BurstDampenedAgePolicy"/>: <c>bulk</c> steps 100ms per write, inside that policy's own
    /// 5-second window, so the whole import arbitrates within ONE burst; <c>spaced</c> steps 10s per write,
    /// outside it, so every write starts its own burst and the damping degenerates to per-write ticks. The
    /// bulk arm replaced one driven by the real clock and reproduces it to six decimal places, so the
    /// substitution fixed the figure's PROVENANCE without moving the figure.</para>
    /// <para>rawA=8 &gt; rawB=4 in BOTH arms, so raw selects phase A either way - the regime the corpus
    /// declares wrong. Weighted AGREES with raw under bulk and DISAGREES under spaced, selecting phase B.
    /// Every weighted figure is read the instant BEFORE the corpus's final query, which would otherwise
    /// reinforce all of phase B to a degenerate retrievability of exactly 1 and hide the real spread; both
    /// sums, both counts and both ranges sit in the assertion messages, so a regression prints the
    /// measurement rather than only PASS/FAIL.</para></summary>
    [Fact]
    public async Task Raw_and_weighted_support_may_agree_or_disagree_depending_on_write_pacing()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default with { RoutineCount = 12 }, Seed);
        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;

        // The corpus's own declared answer, restated directly against THIS corpus instance.
        // MemoryCorpusTests.The_final_routine_query_names_phase_B_only_and_never_phase_A pins the same fact
        // and is no broader: a single [Fact] at RoutineCount=12, on its own seed. The property-based test
        // beside it (Phase_A_is_the_larger_regime_for_every_legal_RoutineCount) pins the SPLIT, not this.
        var finalRoutineQuery = corpus.Steps.OfType<CorpusQuery>()
            .Last(q => q.RelevantIds.Any(id => id.StartsWith("routine", StringComparison.Ordinal)));
        Assert.All(finalRoutineQuery.RelevantIds,
            id => Assert.StartsWith("routineB", id, StringComparison.Ordinal));

        // The step IS the arm, and it is the only thing that differs between the two: same shipped policy,
        // same corpus, same seed. 100ms sits inside the burst window and 10s sits outside it.
        var burst = await RunArmAsync(corpus, firstWrite, finalRoutineQuery, TimeSpan.FromMilliseconds(100));
        var stepped = await RunArmAsync(corpus, firstWrite, finalRoutineQuery, TimeSpan.FromSeconds(10));

        // Both arms: the enumeration taken AFTER the full replay (including the final query) is
        // DEGENERATE for weightedB - the final query recalls and thereby reinforces all four phase-B
        // members, pinning every one at Age <= 0 -> retrievability exactly 1, so weightedB there carries
        // no information rawB did not already have. Documented directly rather than only in prose.
        Assert.Equal(burst.AfterFullReplay.RawB, burst.AfterFullReplay.WeightedB, precision: 6);
        Assert.Equal(stepped.AfterFullReplay.RawB, stepped.AfterFullReplay.WeightedB, precision: 6);

        // The BEFORE-final-query snapshot is the robust figure and the one every verdict below reads.
        var b = burst.BeforeFinalQuery;
        var s = stepped.BeforeFinalQuery;

        // Under BULK pacing: raw AND weighted both select phase A, which the corpus declares WRONG - they
        // AGREE, on the wrong answer. The whole import lands inside one burst window, so the damping
        // arbitrates within a single bulk ingest instead of protecting anything written before it.
        Assert.True(b.RawA > b.RawB, $"bulk raw: A={b.RawA} B={b.RawB}");
        Assert.True(b.WeightedA > b.WeightedB,
            $"bulk weighted: A={b.WeightedA:F6} (n={b.RawA}, range={RangeText(b.PhaseA)}) "
            + $"B={b.WeightedB:F6} (n={b.RawB}, range={RangeText(b.PhaseB)})");

        // Under SPACED pacing (>= 5s between writes): raw still selects phase A, but weighted now selects
        // phase B, the corpus's correct answer. Disagreement.
        Assert.True(s.RawA > s.RawB, $"spaced raw: A={s.RawA} B={s.RawB}");
        Assert.True(s.WeightedB > s.WeightedA,
            $"spaced weighted: A={s.WeightedA:F6} (n={s.RawA}, range={RangeText(s.PhaseA)}) "
            + $"B={s.WeightedB:F6} (n={s.RawB}, range={RangeText(s.PhaseB)})");

        // Asserted rather than described: "phase B outranks phase A per-member" was prose, and prose rots.
        // Min of B against max of A is the strong form - no member of either regime overlaps - and it is
        // what makes the sum's flip a question about CARDINALITY alone rather than about which regime is
        // better remembered. It holds under both pacings; only the sum's verdict moves between them.
        Assert.True(Outranks(b), $"bulk per-member: A={RangeText(b.PhaseA)} B={RangeText(b.PhaseB)}");
        Assert.True(Outranks(s), $"spaced per-member: A={RangeText(s.PhaseA)} B={RangeText(s.PhaseB)}");
    }

    // The emptiness guard is load-bearing for the same reason RangeText's is: Assert.True evaluates its
    // message eagerly, and an empty bucket would throw out of Min()/Max() before the assertion reported.
    private static bool Outranks(RegimeSnapshot snapshot) =>
        snapshot.PhaseA.Count > 0 && snapshot.PhaseB.Count > 0
        && snapshot.PhaseB.Min() > snapshot.PhaseA.Max();

    /// <summary>Replays the whole corpus on a clock stepped <paramref name="perWrite"/> before every write,
    /// and returns both snapshots. The step is the arm: it decides whether consecutive writes fall inside
    /// <see cref="BurstDampenedAgePolicy"/>'s window or start fresh bursts.</summary>
    private static async Task<(RegimeSnapshot BeforeFinalQuery, RegimeSnapshot AfterFullReplay)> RunArmAsync(
        MemoryCorpus corpus, MemoryWrite firstWrite, CorpusQuery finalRoutineQuery, TimeSpan perWrite)
    {
        // One `now` behind three readers, so the wall clock is unreachable from this replay. The engine's own
        // clock is passed for completeness only - it is read by PruneAsync, which this replay never calls.
        var now = DateTimeOffset.UnixEpoch;
        Func<DateTimeOffset> clock = () => now;
        var store = new InMemoryMemoryGraphStore(clock);
        const string engineName = "gist-support";
        var engine = new GraphMemoryEngine(engineName, store,
            agePolicies: [new BurstDampenedAgePolicy(clock: clock)], clock: clock);
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
                    now += perWrite;
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
