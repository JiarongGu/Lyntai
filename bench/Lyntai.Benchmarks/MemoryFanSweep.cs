using System.Collections.Concurrent;
using System.Diagnostics;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// One factor: <see cref="ReciprocalRankFusionOptions.DiagnosticityWeight"/> — ACT-R's FAN EFFECT, added
/// 2026-08-15 and shipped at <c>0</c> because nothing had measured it.
///
/// <para><b>Why the axis exists.</b> Nothing in this engine consulted <see cref="GraphNode.Degree"/>, so a
/// node with fifty neighbours contributed exactly as much to a recall as a node with one. Anderson's
/// declarative-memory model has association strength fall as a cue gains associates, and the
/// information-theoretic restatement is the one this library should be judged on: a node adjacent to
/// everything discriminates nothing. This engine BUILDS hubs deliberately — subject annotation exists to
/// produce shared handles — so the condition is not hypothetical.</para>
///
/// <para><b>What this run can and cannot settle.</b> It measures whether the knob MOVES recall quality on
/// this corpus and in which direction, which is what a default needs before it moves off zero (D49, D54:
/// a ranking constant changes on a measurement, never on an argument). It does NOT establish the best value
/// — the arms are a coarse ladder, not a search — and it inherits every blind spot of the corpus it runs on,
/// including that relevance here is LEXICAL by construction (<c>TASKS.md</c> Part 69).</para>
///
/// <para><b>The confound worth naming up front.</b> Degree is not independent of relevance in this corpus:
/// co-activation links whatever a recall returned together, so a frequently-returned entry ACCUMULATES
/// degree. Penalising degree therefore partly penalises "has been useful before", which is the opposite of
/// the intent. A shape with annotation off (no deliberate hubs) and one with it on would separate them; this
/// run reports the aggregate and says so rather than implying a clean read.</para>
/// </summary>
internal static class MemoryFanSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 20;
    private const int QueryLimit = 10;

    private sealed record Arm(string Label, double Weight);

    private sealed record Shape(string Label, CorpusShape Value);

    private sealed record Row(int Seed, string Shape, string Class, string Arm,
        double MissRate, double PollutionRate);

    public static async Task<int> RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();

        // A coarse ladder around the other signals' own weight of 1 — off, a half-strength voice, an equal
        // voice, and a dominant one. Enough to see direction and rough magnitude; not a search.
        Arm[] arms = [new("off", 0), new("0.5", 0.5), new("1.0", 1.0), new("2.0", 2.0)];

        var baseline = CorpusShape.Default;
        var shapes = new Shape[]
        {
            new("baseline", baseline),
            new("many-candidates", baseline with { CandidateCount = 40 }),
            new("high-noise", baseline with { NoiseDensity = 40 }),
            new("critical-rare", baseline with { CriticalRarity = 12 }),
        };

        PrintPreamble(arms, shapes);

        var rows = new ConcurrentBag<Row>();

        async ValueTask RunOneAsync(int seed, Shape shape, Arm arm)
        {
            var corpus = MemoryCorpus.Generate(shape.Value, seed);

            // Every other constant inherited from ONE shared options object, so a cell differs from its
            // neighbour in exactly the swept value — the discipline MemoryPolicySweep applies to its own axis.
            var ranking = new ReciprocalRankFusionPolicy(new ReciprocalRankFusionOptions
            {
                RelativeFloor = new MultiplicativeRankingOptions().RelativeFloor,
                DiagnosticityWeight = arm.Weight,
            });

            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine("fan", new SqliteMemoryGraphStore(db.Factory),
                retrievability: new DsrRetrievability(), agePolicies: [new PerWriteAgePolicy()], ranking: ranking);

            var replay = await MemoryPolicySweep.ReplayAsync(corpus, engine, QueryLimit);
            foreach (var (cls, quality) in replay.ByClass)
                rows.Add(new Row(seed, shape.Label, cls, arm.Label,
                    quality.Average(q => q.MissRate), quality.Average(q => q.PollutionRate)));
        }

        var seeds = Enumerable.Range(0, SeedCount).Select(i => BaseSeed + i).ToList();
        await Parallel.ForEachAsync(
            from seed in seeds
            from shape in shapes
            from arm in arms
            select (seed, shape, arm),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            async (item, _) => await RunOneAsync(item.seed, item.shape, item.arm));

        PrintComparison([.. rows], shapes, arms);
        PrintNotSwept();
        Console.WriteLine($"\nWall clock: {stopwatch.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    private static void PrintPreamble(Arm[] arms, Shape[] shapes)
    {
        Console.WriteLine("memory-fan — ACT-R's fan effect as a fused RRF signal (DiagnosticityWeight)\n");
        Console.WriteLine($"  arms:   {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine($"  shapes: {string.Join(", ", shapes.Select(s => s.Label))}");
        Console.WriteLine($"  seeds:  {SeedCount}, limit {QueryLimit}");
        Console.WriteLine();
        Console.WriteLine("  `off` is the SHIPPED default and must reproduce the policy sweep's own numbers;");
        Console.WriteLine("  a difference there is an instrument bug, not a finding.\n");
    }

    private static void PrintComparison(Row[] rows, Shape[] shapes, Arm[] arms)
    {
        foreach (var shape in shapes)
        {
            foreach (var cls in rows.Where(r => r.Shape == shape.Label).Select(r => r.Class).Distinct().Order())
            {
                foreach (var arm in arms)
                {
                    var cells = rows.Where(r => r.Shape == shape.Label && r.Class == cls && r.Arm == arm.Label).ToList();
                    if (cells.Count == 0) continue;
                    Console.WriteLine($"    {shape.Label}/{cls}/w={arm.Label}: {cells.Count} seed(s), " +
                        $"mean miss={cells.Average(c => c.MissRate):F3} " +
                        $"pollution={cells.Average(c => c.PollutionRate):F3}");
                }
            }
        }
    }

    private static void PrintNotSwept()
    {
        Console.WriteLine("\nNOT swept (stated rather than left implicit):");
        Console.WriteLine("  - The BEST value. Four coarse arms show direction and rough magnitude; they are");
        Console.WriteLine("    not a search, and no default should move off zero on this run alone.");
        Console.WriteLine("  - Degree's CONFOUND with usefulness. Co-activation links whatever a recall");
        Console.WriteLine("    returned together, so a frequently-returned entry accumulates degree —");
        Console.WriteLine("    penalising degree partly penalises \"has been useful before\". Separating them");
        Console.WriteLine("    needs an annotation-off vs annotation-on pair, which this run does not do.");
        Console.WriteLine("  - The MultiplicativeRankingPolicy arm — the fan term is on RRF only, because RRF");
        Console.WriteLine("    is the registered default and a second implementation before a measurement");
        Console.WriteLine("    would be two unmeasured knobs instead of one.");
        Console.WriteLine("  - Lexical ground truth: this corpus defines relevance by id-in-query, so a");
        Console.WriteLine("    diagnosticity gain that is really a semantic one cannot show up here at all.");
    }
}
