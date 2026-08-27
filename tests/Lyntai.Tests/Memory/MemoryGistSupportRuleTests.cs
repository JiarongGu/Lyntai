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
/// <see cref="CorpusShape.RoutineCount"/> exists specifically to separate them: phase A (larger, OLDER) vs.
/// phase B (smaller, NEWER), with the corpus itself declaring phase B the correct answer. This test measures
/// whether the two rules actually disagree on that corpus. If they do not, no policy seam is needed - this
/// test IS the deliverable, per local/superpowers/specs/2026-08-27-gist-tier-design.md section 9a.
/// <para><b>Measured</b> (RoutineCount=12, seed 12345, undamped PerWriteAgePolicy, recall limit 10):
/// phase A has 8 members, retrievability range [0.156078621, 0.173555437], rawA=8, weightedA=1.301059416.
/// Phase B has 4 members, retrievability range [1.0, 1.0] exactly (all four were reinforced by the final
/// routine query itself, which fires as the corpus's very last write-adjacent step, so their age relative
/// to the read is zero), rawB=4, weightedB=4.000000000. Raw selects A (8 &gt; 4); weighted selects B
/// (4.000000000 &gt; 1.301059416) - the two rules DISAGREE on this corpus. Phase A's own moderate
/// (rather than near-zero) retrievability is exactly the reinforcement-then-further-decay effect a hand
/// estimate cannot see: the phase-A-era query reinforces phase A once, then the rest of the corpus's
/// writes age it further with no second touch.</para></summary>
public class MemoryGistSupportRuleTests
{
    // Traceable to the plan's own worked example (local/superpowers/plans/2026-08-27-gist-support-rule.md),
    // not required for correctness - any fixed seed pins a reproducible corpus.
    private const int Seed = 12345;

    [Fact]
    public async Task Raw_and_weighted_support_select_different_regimes()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default with { RoutineCount = 12 }, Seed);
        var store = new InMemoryMemoryGraphStore();
        const string engineName = "gist-support";
        // PerWriteAgePolicy, not the engine's own damped default: this replay is a fast in-process loop that
        // lands entirely inside BurstDampenedAgePolicy's wall-clock burst window, which would flatten the
        // very age gap between phase A and phase B this measurement depends on - the identical substitution
        // MemoryDefaultRecallQualityTests makes, for the same reason.
        var engine = new GraphMemoryEngine(engineName, store, agePolicies: [new PerWriteAgePolicy()]);

        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;
        const int limit = 10;

        // IN TIMELINE ORDER - writes and queries interleaved, exactly as MemoryCorpus's own ordering
        // contract requires. The phase-A-era routine query fires here, mid-replay, and reinforces whatever
        // phase-A members it recalls; skipping straight to a bulk enumeration would silently drop that
        // effect and measure a corpus this one is not.
        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    await engine.RememberAsync(w.Write);
                    break;
                case CorpusQuery q:
                    await engine.RecallAsync(
                        new MemoryQuery(firstWrite.TaskKey, firstWrite.Scope, q.Text, Limit: limit));
                    break;
            }
        }

        // The corpus's own declared answer, restated here rather than only trusted from elsewhere: the
        // final routine query names phase B alone.
        // MemoryRoutineClassTests.The_final_routine_query_names_phase_B_only_and_never_phase_A pins the
        // same fact more strongly (property-based); this is a direct check against THIS test's own corpus
        // instance.
        var finalRoutineQuery = corpus.Steps.OfType<CorpusQuery>()
            .Last(q => q.RelevantIds.Any(id => id.StartsWith("routine", StringComparison.Ordinal)));
        Assert.All(finalRoutineQuery.RelevantIds,
            id => Assert.StartsWith("routineB", id, StringComparison.Ordinal));

        // Enumerate with a null-query SeedAsync, which does NOT reinforce what it returns (unlike
        // RecallAsync) - reading the stored state must not itself perturb the measurement.
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

        var rawA = phaseA.Count;
        var rawB = phaseB.Count;
        var weightedA = phaseA.Sum();
        var weightedB = phaseB.Sum();

        // The point of this test is the number, not the boolean - both sums, both counts and both ranges
        // are in the assertion messages so a future regression prints the measurement, not just PASS/FAIL.
        Assert.True(weightedB > weightedA,
            $"weighted: A={weightedA:F6} (n={rawA}, range=[{phaseA.Min():F6},{phaseA.Max():F6}]) "
            + $"B={weightedB:F6} (n={rawB}, range=[{phaseB.Min():F6},{phaseB.Max():F6}])");
        Assert.True(rawA > rawB, $"raw: A={rawA} B={rawB}");
    }
}
