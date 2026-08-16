using System.Collections.Concurrent;
using System.Diagnostics;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Verification;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Benchmarks;

/// <summary>
/// What a MODEL IN THE LOOP is worth to recall quality — the one memory mechanism aimed squarely at
/// <c>PollutionRate</c>, and the only 3.0 seam with no sweep of its own until now.
///
/// <para><b>Why this exists.</b> Every recall-quality figure this repository publishes is MODEL-FREE:
/// <c>MemoryPolicySweep</c> wires no annotator and no verifier, so its numbers are the lexical floor, not
/// what a consumer who registered an LLM would see. That floor reads worse than it is —
/// <c>critical-rare</c>'s measured pollution of ~0.805 sits half a point above the structural floor
/// <c>(limit - |relevant|) / limit = 0.800</c> that <see cref="RecallQuality"/>'s own docs describe, because
/// a 10-slot page over a 2-target ground truth is 80% non-relevant no matter how perfect the ranker is.
/// The only way to beat that floor is to RETURN FEWER THINGS, which is exactly what
/// <see cref="IMemoryVerificationPolicy"/> plus <c>GraphMemoryOptions.VerificationFilters</c> does.</para>
///
/// <para><b>The verifier here is PERFECT by construction, and that is the point rather than a flaw</b> —
/// the same stance <c>MemoryAnnotationSweep</c> takes for its annotator. It reads the corpus's own ground
/// truth, so this measures the MECHANISM'S CEILING, not any model's accuracy. A real judge (Ollama, a local
/// CLI, a hosted model) can only do worse. If the ceiling is small, no prompt and no model will rescue it;
/// if it is large, the remaining question is a model's accuracy, which is a different measurement needing a
/// live model and a token budget (<c>TASKS.md</c> Part 65).</para>
///
/// <para><b>Three arms, because the seam has two distinct postures</b> and conflating them would hide which
/// half pays: <c>off</c> (no verifier at all — today's shipped default), <c>reorder</c> (a verdict that only
/// informs reinforcement and ranking), and <c>filter</c> (<c>VerificationFilters = true</c>, where a
/// candidate the judge rejected is dropped from the page). Only the third can move pollution below the
/// structural floor; the second is measured so the difference between "the judge is consulted" and "the
/// judge is obeyed" is visible rather than assumed.</para>
/// </summary>
internal static class MemoryVerificationSweep
{
    private const int BaseSeed = 12345;
    private const int SeedCount = 20;
    private const int QueryLimit = 10;

    /// <summary>Judges against the corpus's own declared relevant set — a ceiling, not a model.
    ///
    /// <para><b>Keyed by query TEXT, and the honest caveat that carries.</b> <c>MemoryCorpus</c> is explicit
    /// that a query's relevant set belongs to THAT step and not to another with the same words. Where one
    /// text recurs with different sets this oracle takes their UNION, which can only make it look better than
    /// a step-exact oracle would — so the ceiling reported here is an upper bound on an upper bound. The
    /// alternative (popping a queue in replay order) desynchronises silently the moment anything other than a
    /// corpus query reaches the verifier, which is a worse failure than a disclosed over-estimate. The
    /// preamble prints how many texts actually collided, so a reader can see whether the caveat bit at all.
    /// </para></summary>
    private sealed class PerfectVerifier(IReadOnlyDictionary<string, HashSet<string>> relevantByQuery)
        : IMemoryVerificationPolicy
    {
        public Task<MemoryVerification> VerifyAsync(
            MemoryVerificationRequest request, CancellationToken ct = default)
        {
            if (!relevantByQuery.TryGetValue(request.Query, out var relevant))
                return Task.FromResult(MemoryVerification.NoOpinion);

            var ids = request.Candidates
                .Where(c => relevant.Contains(CorpusId(c.Headline)))
                .Select(c => c.Id)
                .ToList();

            // A genuine "none of these answered it" is a real verdict and must NOT be reported as NoOpinion —
            // the engine treats those differently, and collapsing them is the defect MemoryVerification's own
            // docs warn about.
            return Task.FromResult(new MemoryVerification(ids));
        }
    }

    /// <summary>The corpus id embedded in an entry's text — "{leading} {id} …". Matches
    /// <c>MemoryPolicySweep</c>'s own extractor; a headline is truncated at its END so the id survives.</summary>
    private static string CorpusId(string text)
    {
        var parts = text.Split(' ', 3);
        return parts.Length >= 2 ? parts[1] : text;
    }

    private sealed record Arm(string Label, bool Verified, bool Filters);

    private sealed record Shape(string Label, CorpusShape Value);

    private sealed record Row(int Seed, string Shape, string Class, string Arm,
        double MissRate, double PollutionRate);

    public static async Task<int> RunAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        var rrf = new ReciprocalRankFusionPolicy(
            new ReciprocalRankFusionOptions { RelativeFloor = new MultiplicativeRankingOptions().RelativeFloor });

        Arm[] arms =
        [
            new("off", false, false),
            new("reorder", true, false),
            new("filter", true, true),
        ];

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
        var collisions = 0;

        async ValueTask RunOneAsync(int seed, Shape shape, Arm arm)
        {
            var corpus = MemoryCorpus.Generate(shape.Value, seed);

            // The oracle's map, built from this corpus's OWN steps — union on a repeated text, counted so the
            // caveat above is measured rather than merely stated.
            var relevantByQuery = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var step in corpus.Steps.OfType<CorpusQuery>())
            {
                if (relevantByQuery.TryGetValue(step.Text, out var existing))
                {
                    if (!existing.SetEquals(step.RelevantIds)) Interlocked.Increment(ref collisions);
                    existing.UnionWith(step.RelevantIds);
                }
                else
                {
                    relevantByQuery[step.Text] = [.. step.RelevantIds];
                }
            }

            using var db = new MemoryPolicySweep.SweepDb();
            var engine = new GraphMemoryEngine("verification", new SqliteMemoryGraphStore(db.Factory),
                options: new GraphMemoryOptions { VerificationFilters = arm.Filters },
                retrievability: new DsrRetrievability(), agePolicies: [new PerWriteAgePolicy()], ranking: rrf,
                verification: arm.Verified ? new PerfectVerifier(relevantByQuery) : null);

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
        PrintNotSwept(collisions);
        Console.WriteLine($"\nWall clock: {stopwatch.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    private static void PrintPreamble(Arm[] arms, Shape[] shapes)
    {
        Console.WriteLine("memory-verification — what a PERFECT judge is worth, as a ceiling\n");
        Console.WriteLine($"  arms:   {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine($"  shapes: {string.Join(", ", shapes.Select(s => s.Label))}");
        Console.WriteLine($"  seeds:  {SeedCount}, limit {QueryLimit}");
        Console.WriteLine();
        Console.WriteLine("  Reading the numbers: PollutionRate has a STRUCTURAL FLOOR of");
        Console.WriteLine("  (limit - |relevant|) / limit, so a 10-slot page over a 2-target ground truth");
        Console.WriteLine("  floors at 0.800 however perfect the ranking. Only the `filter` arm can go");
        Console.WriteLine("  below it, because only it returns FEWER things. A `reorder` arm matching `off`");
        Console.WriteLine("  on pollution is therefore the expected result, not a broken run.\n");
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
                    Console.WriteLine($"    {shape.Label}/{cls}/{arm.Label}: {cells.Count} seed(s), " +
                        $"mean miss={cells.Average(c => c.MissRate):F3} " +
                        $"pollution={cells.Average(c => c.PollutionRate):F3}");
                }
            }
        }
    }

    private static void PrintNotSwept(int collisions)
    {
        Console.WriteLine("\nNOT swept (stated rather than left implicit — a silent gap reads as");
        Console.WriteLine("\"covered everything\"):");
        Console.WriteLine("  - A REAL judge's ACCURACY. This verifier is perfect by construction, so every");
        Console.WriteLine("    number here is the MECHANISM'S CEILING. A model can only do worse; measuring");
        Console.WriteLine("    how much worse needs a live model and a token budget (TASKS.md Part 65).");
        Console.WriteLine("  - The judge's COST. A verified recall spends a model call over up to");
        Console.WriteLine("    VerificationDepth candidates. That is a latency and money question this");
        Console.WriteLine("    harness does not measure at all, and it is the reason the seam ships OFF.");
        Console.WriteLine("  - VerificationDepth as an axis — every arm uses the shipped default.");
        Console.WriteLine($"  - Step-exact ground truth: the oracle keys on query TEXT and unions a repeated");
        Console.WriteLine($"    text's relevant sets. Collisions observed this run: {collisions}. Non-zero");
        Console.WriteLine("    means the ceiling here is an over-estimate by that much.");
    }
}
