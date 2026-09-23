using System.Diagnostics;
using System.Globalization;

using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Benchmarks;

/// <summary>
/// <c>memory-consolidation</c> — does an OFFLINE pass over the stored graph find same-entity links the
/// write path missed? Nothing runs offline today, so two memories that never co-occurred never link
/// (<c>docs/task-archive.md</c> Part 271).
///
/// <para><b>The write path already links by similarity.</b> <c>GraphMemoryEngine</c> gives each write a
/// <c>similar</c> edge to its nearest PREDECESSORS at cosine ≥ <c>GraphMemoryOptions.MinSimilarity</c>, so an
/// offline cosine pass is the same detector and can only add what that search's cap cut. The baseline arm is
/// therefore an engine WITH an embedder and no annotator: the most headroom consolidation is ever offered,
/// since a real annotator only removes some of it.</para>
///
/// <para><b>The fixture is <c>memory-annotation-drift</c>'s, never <c>MemoryCorpus</c>.</b> The corpus's
/// attribute cluster shares a template suffix and nothing else, so a similarity detector would link it
/// through the template. Here the first fact names an entity and the rest refer to it obliquely, written
/// interleaved — the case a consolidation pass would exist for.</para>
///
/// <para><b>Three instrument checks withhold the verdict</b>: the perfect-annotator arm reads headroom 0;
/// every write-time edge of the baseline arm clears the floor through <see cref="VectorMath.Cosine"/>, so the
/// bench sees the engine's vectors; and every edge reads back symmetric.</para>
/// </summary>
internal static class MemoryConsolidationSweep
{
    private const string Engine = "consolidation";
    private const string TaskKey = "t";

    /// <summary>A full round-robin over the fixture's eight clusters — the order a real stream has. The
    /// consecutive order hands an adjacency rule its answer (<c>annotation-drift-recency-gap</c>).</summary>
    private const int Gap = 7;

    // PRE-REGISTERED before the first run: a detector must reach LiftBar × the base rate AND recover
    // RecoveryBar of the headroom, in both languages. Judgement calls, never values read off a run.
    private const double LiftBar = 2.0;
    private const double RecoveryBar = 0.25;

    private static readonly double[] Thetas = [0.60, 0.70, 0.80];

    // POST-HOC, added after the first run found the whole English distribution below the write floor, so the
    // pre-registered rows could not fire there. Printed, never counted toward the verdict.
    private static readonly double[] PostHocThetas = [0.40, 0.45, 0.50];

    private static readonly int[] CommonNeighbours = [1, 2];

    // Lower than `--corroborate`'s 0.35/0.50: those compared whole turns, and these are clauses of five to
    // ten words, where two shared content tokens already read ~0.25.
    private static readonly double[] Overlaps = [0.20, 0.35];

    private sealed record Detector(string Label, Func<(int A, int B), bool> Proposes, bool PostHoc = false);

    public static async Task<int> RunAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        var vectorProvider = await SweepDoubles.TryRealVectorProviderAsync(http, "memory-consolidation");
        if (vectorProvider is null) return 1;

        var stopwatch = Stopwatch.StartNew();
        var floor = new GraphMemoryOptions().MinSimilarity;
        PrintPreamble(floor);

        var controlsOk = true;
        var passing = new List<HashSet<string>>();
        foreach (var (language, chinese) in new[] { ("english", false), ("chinese", true) })
        {
            var order = MemoryAnnotationDriftSweep.Order(MemoryAnnotationDriftSweep.Fixture(chinese), Gap);
            var facts = order.Select(o => o.Fact).ToList();
            var cluster = order.Select(o => o.Cluster).ToList();
            var vectors = await vectorProvider.EmbedAsync(facts);
            var tokens = facts.Select(Tokens).ToList();

            var all = (from a in Enumerable.Range(0, facts.Count)
                       from b in Enumerable.Range(a + 1, facts.Count - a - 1)
                       select (A: a, B: b)).ToList();
            var truth = all.Where(p => cluster[p.A] == cluster[p.B]).ToHashSet();
            var cos = all.ToDictionary(p => p, p => VectorMath.Cosine(vectors[p.A], vectors[p.B]));

            var baseline = await IngestAsync(facts, vectorProvider, annotation: null);
            var keyByFact = order.ToDictionary(o => o.Fact, o => o.Cluster, StringComparer.Ordinal);
            var perfect = await IngestAsync(facts, vectorProvider,
                new MemoryAnnotationDriftSweep.PerfectAnnotator(keyByFact));

            Console.WriteLine();
            Console.WriteLine($"=== {language}: {facts.Count} facts, {all.Count} pairs, {truth.Count} same-entity " +
                $"(base rate over all pairs {(double)truth.Count / all.Count:P1}) ===");

            var perfectHeadroom = truth.Count(p => !perfect.Edges.Contains(p));
            var minEdgeCos = baseline.Edges.Count == 0 ? double.NaN : baseline.Edges.Min(p => cos[p]);
            var c1 = perfectHeadroom == 0;
            var c2 = baseline.Edges.Count > 0 && minEdgeCos >= floor - 1e-9;
            var c3 = baseline.Symmetric && perfect.Symmetric;
            controlsOk &= c1 && c2 && c3;
            Console.WriteLine($"  C1 perfect-annotator arm leaves no same-entity pair unlinked: {perfectHeadroom} " +
                $"unlinked  {(c1 ? "ok" : "<== SUSPECT")}");
            Console.WriteLine($"  C2 every baseline write-time edge clears the floor {floor:0.00} by the bench's cosine: " +
                $"{baseline.Edges.Count} edges, min {minEdgeCos:0.000}  {(c2 ? "ok" : "<== SUSPECT")}");
            Console.WriteLine($"  C3 every edge reads back symmetric: {(c3 ? "ok" : "<== SUSPECT")}");

            var rows = new HashSet<string>(StringComparer.Ordinal);
            foreach (var (arm, edges, isBaseline) in new[]
                     { ("similar (baseline)", baseline.Edges, true), ("similar+perfect", perfect.Edges, false) })
            {
                var headroom = truth.Where(p => !edges.Contains(p)).ToHashSet();
                var candidates = all.Where(p => !edges.Contains(p)).ToList();
                var baseRate = candidates.Count == 0 ? 0 : (double)headroom.Count / candidates.Count;
                var linkedTrue = edges.Count(truth.Contains);

                Console.WriteLine();
                Console.WriteLine($"  --- arm: {arm} ---");
                Console.WriteLine($"  write time linked {edges.Count} pairs, {linkedTrue} same-entity " +
                    $"(precision {Ratio(linkedTrue, edges.Count)}, recall {Ratio(linkedTrue, truth.Count)})");
                Console.WriteLine($"  headroom {headroom.Count} of {truth.Count} same-entity pairs unlinked; " +
                    $"{candidates.Count} candidate pairs; base rate {baseRate:P1}");
                if (headroom.Count > 0)
                {
                    var pos = candidates.Where(headroom.Contains).Select(p => cos[p]).ToList();
                    var neg = candidates.Where(p => !headroom.Contains(p)).Select(p => cos[p]).ToList();
                    Console.WriteLine($"  cosine over candidates: headroom p50 {Percentile(pos, 0.5):0.000} / " +
                        $"cross-entity p50 {Percentile(neg, 0.5):0.000}; separability AUC {Auc(pos, neg):0.000}");
                }

                Console.WriteLine($"  {"detector",-26} {"proposed",9} {"true",5} {"precision",10} {"lift",6} {"recovered",10}");
                foreach (var detector in Detectors(cos, tokens, edges, facts.Count))
                {
                    var proposed = candidates.Where(p => detector.Proposes(p)).ToList();
                    var hits = proposed.Count(headroom.Contains);
                    var precision = proposed.Count == 0 ? 0 : (double)hits / proposed.Count;
                    var lift = baseRate == 0 ? 0 : precision / baseRate;
                    var recovered = headroom.Count == 0 ? 0 : (double)hits / headroom.Count;
                    var passes = lift >= LiftBar && recovered >= RecoveryBar;
                    if (passes && isBaseline && !detector.PostHoc) rows.Add(detector.Label);
                    Console.WriteLine($"  {detector.Label,-26} {proposed.Count,9} {hits,5} {Ratio(hits, proposed.Count),10} " +
                        $"{lift,6:0.00} {(headroom.Count == 0 ? "—" : $"{recovered:P0}"),10}{(passes ? "  PASSES" : "")}");
                }
            }
            passing.Add(rows);
        }

        var both = passing.Aggregate((a, b) => [.. a.Intersect(b)]);
        Console.WriteLine();
        Console.WriteLine(!controlsOk
            ? "VERDICT WITHHELD — an instrument check failed above; no row is readable until it passes."
            : both.Count > 0
                ? $"VERDICT: YES — {string.Join(", ", both)} clears the pre-registered bar in both languages. " +
                  "Next question: is it a write-time KNOB (SimilarityK / MinSimilarity) rather than a seam?"
                : "VERDICT: NO — no detector clears the pre-registered bar in both languages.");
        Console.WriteLine();
        Console.WriteLine($"Wall clock: {stopwatch.Elapsed.TotalSeconds:F1}s; {vectorProvider.Misses} embed call(s), " +
            $"{vectorProvider.Hits} cache hit(s).");
        return 0;
    }

    /// <summary>Ingest through the REAL engine and read back every edge it made, as unordered index pairs.
    /// A replica of the write path's linking would have to be proven to reproduce it; the engine cannot
    /// disagree with itself.</summary>
    private static async Task<(HashSet<(int A, int B)> Edges, bool Symmetric)> IngestAsync(
        IReadOnlyList<string> facts, IModelProvider vectorProvider, IMemoryAnnotationPolicy? annotation)
    {
        using var db = new MemoryPolicySweep.SweepDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = new GraphMemoryEngine(Engine, store, providers: [vectorProvider],
            vectors: new InMemoryVectorStore(), annotation: annotation);

        var ids = new List<long>(facts.Count);
        foreach (var fact in facts)
            ids.Add(long.Parse((await engine.RememberAsync(new MemoryWrite(TaskKey, "s", fact))).Id,
                CultureInfo.InvariantCulture));

        var index = ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        var directed = new HashSet<(int, int)>();
        for (var i = 0; i < ids.Count; i++)
            foreach (var neighbour in await store.NeighboursAsync(Engine, TaskKey, [ids[i]], facts.Count))
                directed.Add((i, index[neighbour.Node.Id]));

        return ([.. directed.Select(e => (Math.Min(e.Item1, e.Item2), Math.Max(e.Item1, e.Item2)))],
            directed.All(e => directed.Contains((e.Item2, e.Item1))));
    }

    /// <summary>The offline pass's candidate rules. Cosine is the write path's own detector without its cap;
    /// common neighbours is the one thing only an offline pass can see — structure that formed AFTER a write;
    /// token overlap is the model-free floor.</summary>
    private static IEnumerable<Detector> Detectors(IReadOnlyDictionary<(int A, int B), double> cos,
        IReadOnlyList<HashSet<string>> tokens, HashSet<(int A, int B)> edges, int count)
    {
        var adjacent = Enumerable.Range(0, count).Select(_ => new HashSet<int>()).ToList();
        foreach (var (a, b) in edges) { adjacent[a].Add(b); adjacent[b].Add(a); }

        foreach (var theta in Thetas)
            yield return new($"cosine ≥ {theta:0.00}", p => cos[p] >= theta);
        foreach (var theta in PostHocThetas)
            yield return new($"cosine ≥ {theta:0.00} post-hoc", p => cos[p] >= theta, PostHoc: true);
        foreach (var k in CommonNeighbours)
            yield return new($"common neighbours ≥ {k}", p => adjacent[p.A].Count(adjacent[p.B].Contains) >= k);
        foreach (var j in Overlaps)
            yield return new($"token Jaccard ≥ {j:0.00}", p => Jaccard(tokens[p.A], tokens[p.B]) >= j);
    }

    /// <summary>Content tokens, script-aware: a Latin run of three or more characters is a word, and a Han
    /// run contributes its character BIGRAMS — a spaceless clause would otherwise be one token that matches
    /// nothing, and the Chinese row would be vacuous rather than measured.</summary>
    private static HashSet<string> Tokens(string text)
    {
        var tokens = new HashSet<string>(StringComparer.Ordinal);
        var start = -1;
        for (var i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && char.IsLetterOrDigit(text[i]) && !IsHan(text[i]))
            {
                if (start < 0) start = i;
                continue;
            }
            if (start >= 0 && i - start >= 3) tokens.Add(text[start..i].ToLowerInvariant());
            start = -1;
            if (i + 1 < text.Length && IsHan(text[i]) && IsHan(text[i + 1])) tokens.Add(text.Substring(i, 2));
        }
        return tokens;
    }

    private static bool IsHan(char c) => c is >= '一' and <= '鿿';

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        var intersection = a.Count(b.Contains);
        return (double)intersection / (a.Count + b.Count - intersection);
    }

    /// <summary>Rank-sum AUC: the chance a random headroom pair outscores a random cross-entity one. 0.5 means
    /// no threshold on this signal can separate them, however it is tuned.</summary>
    private static double Auc(IReadOnlyList<double> positives, IReadOnlyList<double> negatives)
    {
        if (positives.Count == 0 || negatives.Count == 0) return double.NaN;
        double wins = 0;
        foreach (var p in positives)
            foreach (var q in negatives)
                wins += p > q ? 1 : p == q ? 0.5 : 0;
        return wins / ((double)positives.Count * negatives.Count);
    }

    private static double Percentile(List<double> xs, double p)
    {
        if (xs.Count == 0) return double.NaN;
        var sorted = xs.OrderBy(x => x).ToList();
        return sorted[Math.Min(sorted.Count - 1, (int)(p * sorted.Count))];
    }

    private static string Ratio(int hits, int of) => of == 0 ? "—" : $"{(double)hits / of:P1}";

    private static void PrintPreamble(double floor)
    {
        Console.WriteLine();
        Console.WriteLine("memory-consolidation — does an OFFLINE pass find same-entity links the write path missed?");
        Console.WriteLine();
        Console.WriteLine($"  fixture    memory-annotation-drift's 8 entities x 4 facts, interleaved at gap {Gap}; english + chinese");
        Console.WriteLine($"  arms       similar (embedder, no annotator — the most headroom) / similar+perfect (the self-check)");
        Console.WriteLine($"  write path links each write to its nearest predecessors at cosine >= {floor:0.00}");
        Console.WriteLine($"  bar        PRE-REGISTERED: precision >= {LiftBar:0.0}x the base rate AND >= {RecoveryBar:P0} of the");
        Console.WriteLine("             headroom recovered, the same row in BOTH languages");
        Console.WriteLine();
        Console.WriteLine("  NOT measured: recall quality (an edge found is not a recall improved); a REAL annotator,");
        Console.WriteLine("  which sits between the two arms; any corpus but this one — 48 same-entity pairs a language,");
        Console.WriteLine("  synthetic, so read directions rather than magnitudes.");
    }
}
