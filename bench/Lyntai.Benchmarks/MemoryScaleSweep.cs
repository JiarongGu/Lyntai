using System.Diagnostics;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lyntai.Benchmarks;

/// <summary>
/// One factor: how many entries the store holds. The blind spot <c>docs/memory.md</c> §8 concedes in as
/// many words — <i>"Scale. Nothing exceeds a few hundred entries, single-threaded."</i>
///
/// <para><b>Why the existing benchmark does not already cover this.</b>
/// <see cref="MemoryRecallBenchmarks"/> does run 1k/10k/100k rows, and it runs them against
/// <see cref="SqliteMemoryStore"/> — <c>IMemoryStore</c>, the task-scoped KEYWORD store. The graph engine is
/// a different surface with a different write path (an upsert, an FTS mirror, co-activation edges, a review
/// log) and a different read path (seed, traverse, rank), and none of it had ever been measured above a few
/// hundred entries. A scale number for one store is not a scale number for the other.</para>
///
/// <para><b>This measures COST, not QUALITY, and the two do not mix.</b> Every other <c>memory-*</c> sweep
/// reports miss and pollution over <c>MemoryCorpus</c>, an instrument built to make policies disagree. That
/// corpus is the wrong shape for this question — its size is a property of the shapes it encodes — so this
/// one generates plain entries and reports latency, throughput and bytes. It says nothing about recall
/// quality at scale, which is a separate and harder study.</para>
///
/// <para><b>Everything runs SEQUENTIALLY, unlike every other sweep here.</b> The others fan the whole grid
/// out through <c>Parallel.ForEachAsync</c> because each cell reports a RATE that contention cannot bias.
/// This one reports wall-clock latency, which contention biases directly and silently — so parallelism would
/// not speed the study up, it would make the numbers mean nothing.</para>
/// </summary>
internal static class MemoryScaleSweep
{
    private const int QueryLimit = 10;
    private const int TimedQueries = 200;
    private const int TimedExpansions = 50;

    /// <summary>An entry count to fill the store to.</summary>
    private sealed record Size(string Label, int Entries);

    /// <summary>A recall configuration. The pair exists to SPLIT the cost, not to recommend one: a recall on
    /// shipped defaults reinforces what it returned and writes co-activation edges, so its latency is a read
    /// plus a write-back. Only measuring both says which half grows with the store.</summary>
    private sealed record Arm(string Label, GraphMemoryOptions Options);

    /// <summary><paramref name="HitRate"/> is the CONTROL, not a result: the fraction of timed recalls that
    /// returned anything at all. A recall that matches nothing is fast, and a table of fast empty recalls
    /// looks exactly like a table of good news — the shape <c>pitfalls.md</c> records for a test whose recall
    /// silently returned nothing and exercised only the write path. Anything below 1.000 means these
    /// latencies are partly the cost of missing.</summary>
    private sealed record Row(
        string Size, string Arm, int Entries, double WritesPerSecond,
        double RecallP50, double RecallP95, double RecallP99,
        double ExpandP50, double ExpandP95,
        double ColdStartMs, long DbBytes, double HitRate);

    public static async Task<int> RunAsync(string[] args)
    {
        var stopwatch = Stopwatch.StartNew();

        var sizes = ParseSizes(args);

        // Every constant except the two under test is inherited from ONE options object, so an arm differs
        // from its neighbour in exactly what its label says — the discipline every sweep here applies.
        var shipped = new GraphMemoryOptions();
        Arm[] arms =
        [
            new("shipped", shipped),
            new("read-only", shipped with
            {
                ReinforceOn = MemoryReinforcementActs.None,
                CoActivationCap = 0,
                LogReviews = false,
            }),
        ];

        PrintPreamble(sizes, arms);

        var repeat = ParseRepeat(args);
        var rows = new List<Row>();
        foreach (var size in sizes)
        {
            foreach (var arm in arms)
            {
                // Each repetition gets its own fresh store, so repeats measure run-to-run variance rather
                // than a warming cache. The MEDIAN is what survives: a mean lets one slow run — a background
                // build, a checkpoint — carry the cell.
                var runs = new List<Row>(repeat);
                for (var i = 0; i < repeat; i++) runs.Add(await RunOneAsync(size, arm));

                var row = runs.OrderBy(r => r.RecallP50).ElementAt(runs.Count / 2);
                rows.Add(row);
                PrintRow(row);
                if (repeat > 1)
                    Console.WriteLine($"      ({repeat} runs, p50 recall spread " +
                        $"{runs.Min(r => r.RecallP50):F1}–{runs.Max(r => r.RecallP50):F1}ms)");
            }
        }

        PrintComparison(rows, sizes, arms);
        PrintNotSwept();
        Console.WriteLine($"\nWall clock: {stopwatch.Elapsed.TotalSeconds:F1}s");
        return 0;
    }

    private static async Task<Row> RunOneAsync(Size size, Arm arm)
    {
        using var db = new MemoryPolicySweep.SweepDb();

        GraphMemoryEngine NewEngine() =>
            new("scale", new SqliteMemoryGraphStore(db.Factory), arm.Options,
                retrievability: new DsrRetrievability(), agePolicies: [new PerWriteAgePolicy()]);

        var engine = NewEngine();

        // No embedder and no vector store: an embed per write would dominate the write number and measure
        // the embedder rather than the store. `memory-enrichment` is where that cost is priced.
        var writeStart = Stopwatch.GetTimestamp();
        for (var i = 0; i < size.Entries; i++)
            await engine.RememberAsync(new MemoryWrite("scale", Scope(i), Content(i)));
        var writeSeconds = Stopwatch.GetElapsedTime(writeStart).TotalSeconds;

        var recalls = new List<double>(TimedQueries);
        var references = new List<MemoryRef>(TimedExpansions);
        var hits = 0;
        for (var i = 0; i < TimedQueries; i++)
        {
            // Queries walk the whole corpus rather than repeating one, so the numbers are not a report on
            // however well SQLite happened to cache a single page range.
            var target = i * Math.Max(1, size.Entries / TimedQueries);
            var start = Stopwatch.GetTimestamp();
            var recall = await engine.RecallAsync(
                new MemoryQuery("scale", Scope(target), Query(target), QueryLimit));
            recalls.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);

            if (recall.Items.Count == 0) continue;
            hits++;
            if (references.Count < TimedExpansions) references.Add(recall.Items[0].Reference);
        }

        var expansions = new List<double>(references.Count);
        foreach (var reference in references)
        {
            var start = Stopwatch.GetTimestamp();
            await engine.ExpandAsync(reference);
            expansions.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }

        // COLD START: a fresh engine over the SAME file with the connection pool dropped, which is what a
        // process restart actually costs. Measured after the timed queries on purpose — a warm page cache is
        // exactly what this arm exists to remove.
        SqliteConnection.ClearAllPools();
        var cold = NewEngine();
        var coldStart = Stopwatch.GetTimestamp();
        await cold.RecallAsync(new MemoryQuery("scale", Scope(0), Query(0), QueryLimit));
        var coldMs = Stopwatch.GetElapsedTime(coldStart).TotalMilliseconds;

        return new Row(size.Label, arm.Label, size.Entries,
            writeSeconds > 0 ? size.Entries / writeSeconds : 0,
            Percentile(recalls, 0.50), Percentile(recalls, 0.95), Percentile(recalls, 0.99),
            Percentile(expansions, 0.50), Percentile(expansions, 0.95),
            coldMs, DbBytes(db.DbPath), TimedQueries > 0 ? hits / (double)TimedQueries : 0);
    }

    /// <summary>How many times to run each cell, <c>--repeat 3</c> overriding the default 1.
    /// <para>Default 1 because the full ladder already takes tens of minutes and the numbers this study
    /// exists for — throughput, latency, bytes — are stable across runs. Raise it when the question is a
    /// DIFFERENCE BETWEEN ARMS: those are small, and one cell per arm carries no variance estimate, which is
    /// what makes a negative delta unreadable rather than merely surprising.</para></summary>
    private static int ParseRepeat(string[] args)
    {
        var i = Array.IndexOf(args, "--repeat");
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0 ? n : 1;
    }

    /// <summary>The size ladder, <c>--sizes 1000,10000</c> overriding the default 1k/10k/100k.
    /// <para>It exists so the harness can be exercised end to end in seconds. An instrument is code nothing
    /// else validates, and one whose only run takes an hour gets validated by reading — which is how a
    /// corpus arm once reported <c>0.0000</c> on every shape and looked like a result.</para></summary>
    private static Size[] ParseSizes(string[] args)
    {
        var i = Array.IndexOf(args, "--sizes");
        if (i < 0 || i + 1 >= args.Length)
            return [new("1k", 1_000), new("10k", 10_000), new("100k", 100_000)];

        return [.. args[i + 1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(int.Parse)
            .Where(n => n > 0)
            .Select(n => new Size(Label(n), n))];

        static string Label(int n) => n >= 1_000 && n % 1_000 == 0 ? $"{n / 1_000}k" : n.ToString();
    }

    /// <summary>Twenty scopes, so the corpus is not one scope of 100,000 nor 100,000 of one — either extreme
    /// measures a case no deployment has.</summary>
    private static string Scope(int i) => $"s{i % 20}";

    private static string Content(int i) =>
        $"entry marker{i} covers the deployment checklist and its approval step for component {i % 97}";

    private static string Query(int i) => $"marker{i}";

    /// <summary>Nearest-rank, on a copy — <see cref="List{T}.Sort"/> mutates, and the caller's list is read
    /// again for the next percentile.</summary>
    private static double Percentile(List<double> values, double q)
    {
        if (values.Count == 0) return 0;
        var sorted = new List<double>(values);
        sorted.Sort();
        var rank = (int)Math.Ceiling(q * sorted.Count) - 1;
        return sorted[Math.Clamp(rank, 0, sorted.Count - 1)];
    }

    /// <summary>The db plus its write-ahead log, because a WAL that has not checkpointed holds real bytes and
    /// reporting the main file alone would understate a fresh store by most of its content.</summary>
    private static long DbBytes(string path)
    {
        long total = 0;
        foreach (var file in new[] { path, path + "-wal", path + "-shm" })
        {
            var info = new FileInfo(file);
            if (info.Exists) total += info.Length;
        }
        return total;
    }

    private static void PrintPreamble(Size[] sizes, Arm[] arms)
    {
        Console.WriteLine("memory-scale — what the GRAPH engine costs as the store grows\n");
        Console.WriteLine($"  sizes: {string.Join(", ", sizes.Select(s => s.Label))}");
        Console.WriteLine($"  arms:  {string.Join(", ", arms.Select(a => a.Label))}");
        Console.WriteLine($"  {TimedQueries} timed recalls and {TimedExpansions} expansions per cell, " +
            $"limit {QueryLimit}");
        Console.WriteLine();
        Console.WriteLine("  SEQUENTIAL by design: this reports wall-clock latency, which contention biases");
        Console.WriteLine("  silently, so fanning the grid out would not speed it up — it would void it.\n");
    }

    private static void PrintRow(Row r)
    {
        Console.WriteLine($"    {r.Size}/{r.Arm}: writes {r.WritesPerSecond:F0}/s · " +
            $"recall p50 {r.RecallP50:F1}ms p95 {r.RecallP95:F1}ms p99 {r.RecallP99:F1}ms · " +
            $"expand p50 {r.ExpandP50:F1}ms p95 {r.ExpandP95:F1}ms · " +
            $"cold {r.ColdStartMs:F1}ms · db {r.DbBytes / 1024.0 / 1024.0:F1}MiB " +
            $"({r.DbBytes / (double)r.Entries:F0}B/entry) · hit-rate {r.HitRate:F3}" +
            (r.HitRate < 1 ? "  ← BELOW 1: partly timing MISSES, not recalls" : ""));
    }

    private static void PrintComparison(List<Row> rows, Size[] sizes, Arm[] arms)
    {
        Console.WriteLine("\nHow each number GROWS, which is the question — an absolute latency is a fact");
        Console.WriteLine("about this machine, a growth factor is a fact about the engine.\n");

        foreach (var arm in arms)
        {
            var series = sizes
                .Select(s => rows.FirstOrDefault(r => r.Size == s.Label && r.Arm == arm.Label))
                .Where(r => r is not null)
                .Select(r => r!)
                .ToList();
            if (series.Count < 2) continue;

            var first = series[0];
            foreach (var row in series.Skip(1))
                Console.WriteLine($"    {arm.Label} {first.Size}→{row.Size} " +
                    $"({row.Entries / first.Entries}× entries): " +
                    $"recall p95 ×{Ratio(row.RecallP95, first.RecallP95):F2}, " +
                    $"writes/s ×{Ratio(row.WritesPerSecond, first.WritesPerSecond):F2}, " +
                    $"db ×{Ratio(row.DbBytes, first.DbBytes):F2}");
        }

        Console.WriteLine();
        foreach (var size in sizes)
        {
            var shipped = rows.FirstOrDefault(r => r.Size == size.Label && r.Arm == "shipped");
            var read = rows.FirstOrDefault(r => r.Size == size.Label && r.Arm == "read-only");
            if (shipped is null || read is null) continue;

            // The decomposition this arm pair exists for: how much of a default recall's latency is the READ
            // and how much is the write-back it performs afterwards.
            //
            // REFUSES TO INTERPRET A NEGATIVE, and that is not defensiveness. `read-only` does strictly less
            // work — no touch, no co-activation edges, no review-log row — so it cannot legitimately be
            // SLOWER. A negative delta is therefore a statement about run-to-run variance, not about the
            // engine, and one cell per arm has no variance estimate to net it out against. Printing it as a
            // percentage anyway is how "−7% of the p50" would enter a document as a finding.
            // (`MemorySalienceWeightSweep` refuses its own table the same way when the swept signal cannot
            // vary — same rule, different unmeasurable.)
            var delta = shipped.RecallP50 - read.RecallP50;
            if (delta <= 0)
            {
                Console.WriteLine($"    {size.Label}: NOT READABLE — `read-only` measured " +
                    $"{-delta:F1}ms SLOWER, which it cannot be. One run per cell, so this is variance " +
                    "between two runs rather than a cost. Re-run with --repeat to settle it.");
                continue;
            }

            Console.WriteLine($"    {size.Label}: reinforcement + co-activation costs " +
                $"{delta:F1}ms of the p50 recall ({delta / shipped.RecallP50 * 100:F0}% of it)");
        }
    }

    private static double Ratio(double now, double before) => before > 0 ? now / before : 0;

    private static void PrintNotSwept()
    {
        Console.WriteLine("\nNOT swept (stated rather than left implicit):");
        Console.WriteLine("  - RECALL QUALITY at scale. This corpus has no ground truth and measures cost");
        Console.WriteLine("    only. Whether miss and pollution hold up at 100k is a separate study, and no");
        Console.WriteLine("    number here speaks to it.");
        Console.WriteLine("  - ENRICHMENT. No embedder and no vector store, so writes carry no embed and");
        Console.WriteLine("    recalls no vector search. A deployment with both pays more than this reports,");
        Console.WriteLine("    and `memory-enrichment` is where that cost is priced.");
        Console.WriteLine("  - A MODEL IN THE LOOP. Annotation costs a model call per write and verification");
        Console.WriteLine("    one per recall; both would dominate every number here and neither is wired.");
        Console.WriteLine("  - CONCURRENCY. Single-threaded throughout, which is what makes the latencies");
        Console.WriteLine("    readable — and leaves contention under concurrent writers unmeasured.");
        Console.WriteLine("  - POSTGRES. SQLite only, matching every other sweep here. The engine is the same");
        Console.WriteLine("    on both, the store is not, so these are SQLite's numbers.");
        Console.WriteLine("  - PRUNE and FORGET at scale. Both scan the scope, and neither is timed here.");
        Console.WriteLine("  - Absolute latencies are THIS MACHINE's. Compare the growth factors, not the");
        Console.WriteLine("    milliseconds, against any other run.");
    }
}
