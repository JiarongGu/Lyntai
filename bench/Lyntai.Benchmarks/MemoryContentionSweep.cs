using System.Collections.Concurrent;
using System.Diagnostics;
using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Verification;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Benchmarks;

/// <summary>
/// One backend serving all four model-backed memory seams — annotation, verification, reranking, embedding
/// — wired onto a single <see cref="GraphMemoryEngine"/>, so a cell prices what CONTENTION between them costs
/// rather than any one seam's own accuracy. <c>memory-verification</c>, <c>memory-annotation</c> and
/// <c>memory-enrichment</c> each measure their own seam in isolation; nothing had put all four in front of one
/// server before, and <c>memory-scale</c> named the gap outright in its own NOT-swept list.
///
/// <para><b>Refuses rather than substituting, on the seams it checks.</b> <see cref="BuildRigAsync"/> checks
/// the chat model and the reranker before returning a <see cref="Rig"/> — the same posture
/// <see cref="SweepDoubles.TryRealChatAsync"/> and <see cref="SweepDoubles.TryRealEmbedderAsync"/> already
/// take: an arm that silently ran without a model would look exactly like a fast one, and a bag-of-words
/// stand-in for the embedder was withdrawn once already for producing exactly that illusion (TASKS.md Part
/// 69). The embedder itself is not probed here — <c>devtools/scripts/memory-contention.mjs</c>'s
/// <c>verifyIdentity</c> asserts its vector dimension before any cell runs. This is the positive control
/// every cell here depends on.</para>
///
/// <para>It measures nothing about recall QUALITY, and says so.</para>
/// </summary>
internal static class MemoryContentionSweep
{
    /// <summary>Everything one cell needs, built once so a cell measures the seams rather than the wiring.
    ///
    /// <para><b>The embedder is NOT the caching one.</b> <see cref="SweepDoubles.CachingEmbedder"/> memoizes by
    /// text, which is right for a quality sweep replaying a fixed corpus and fatal here: the embedder is the
    /// most frequent model contact in the library (per write AND per recall) and a cache would report its cost
    /// as zero.</para></summary>
    private sealed record Rig(
        GraphMemoryEngine Engine,
        CrossEncoderReranker Reranker,
        CountingAnnotation Annotation,
        MemoryPolicySweep.SweepDb Db);

    /// <summary>The one HttpClient every seam shares.
    ///
    /// <para><b>`UseProxy = false` is load-bearing, not hygiene.</b> Every other bench here builds a bare
    /// <c>new HttpClient</c>, so proxy resolution runs per call: measured at ~9 ms mean and 34 ms max against
    /// <c>127.0.0.1</c>, and up to <b>2,051 ms</b> against <c>localhost</c>. This sweep reports p95 and p99 of
    /// model calls, and an occasional two-second spike from the CLIENT is indistinguishable from the tail
    /// latency the sweep exists to measure. Disabling it takes the overhead to 0.4 ms.</para></summary>
    private static HttpClient NewHttp() =>
        new(new HttpClientHandler { UseProxy = false }) { Timeout = TimeSpan.FromMinutes(5) };

    private static async Task<Rig?> BuildRigAsync(HttpClient http)
    {
        var chat = await SweepDoubles.TryRealChatAsync(http, "memory-contention");
        if (chat is null) return null;                      // refuses; the reason is already on stderr

        var reranker = new CrossEncoderReranker(http, CrossEncoderReranker.BaseUrl, CrossEncoderReranker.Model);
        if (!await reranker.ReachableAsync())
        {
            Console.Error.WriteLine("memory-contention: no reranker — this arm prices a seam, not a stand-in.");
            return null;
        }

        var embedder = new SweepDoubles.OpenAiCompatibleEmbedder(http, SweepDoubles.BaseUrl, SweepDoubles.Model);
        var clients = new SweepDoubles.BenchClientFactory(chat);

        // The SHIPPED policies, not bench-local ones — an arm has to exercise the shipped prompt, parsing and
        // fail-open behaviour or it measures something no consumer inherits (CrossEncoderRerank's own reasoning).
        var annotation = new CountingAnnotation(new LlmMemoryAnnotationPolicy(clients));
        var verification = new LlmMemoryVerificationPolicy(clients);

        var db = new MemoryPolicySweep.SweepDb();
        var engine = new GraphMemoryEngine("contention", new SqliteMemoryGraphStore(db.Factory),
            new GraphMemoryOptions(), retrievability: new DsrRetrievability(),
            agePolicies: [new PerWriteAgePolicy()], embedder: embedder, vectors: new InMemoryVectorStore(),
            annotation: annotation, verification: verification);

        return new Rig(engine, reranker, annotation, db);
    }

    /// <summary>Counts what annotation actually RETURNED, because a policy that yields zero subjects is a fast
    /// no-op that still costs a model call — and a table of fast no-ops reads exactly like a fast engine. Same
    /// reasoning as <c>CrossEncoderReranker.Audit</c> and <c>MemoryScaleSweep</c>'s hit rate.</summary>
    internal sealed class CountingAnnotation(IMemoryAnnotationPolicy inner) : IMemoryAnnotationPolicy
    {
        private int _calls;
        private int _subjects;

        internal (int Calls, int Subjects) Audit => (Volatile.Read(ref _calls), Volatile.Read(ref _subjects));

        /// <summary>Zeroes both counters. Called once, after <see cref="MemoryContentionSweep.SeedAsync"/>'s
        /// untimed writes, so a seed write's own annotation call does not count toward the TIMED region's
        /// <c>SubjectsPerWrite</c> control.</summary>
        internal void Reset()
        {
            Interlocked.Exchange(ref _calls, 0);
            Interlocked.Exchange(ref _subjects, 0);
        }

        public async Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request,
            CancellationToken ct = default)
        {
            var result = await inner.AnnotateAsync(request, ct).ConfigureAwait(false);
            Interlocked.Increment(ref _calls);
            Interlocked.Add(ref _subjects, result.Subjects.Count);
            return result;
        }
    }

    /// <summary><paramref name="HitRate"/>, <paramref name="SubjectsPerWrite"/> and
    /// <paramref name="DistinctRerankScores"/> are CONTROLS, not results. A recall that matches nothing is
    /// fast and a table of fast misses looks like good news; an annotation that returns no subjects is a
    /// fast no-op that still spent a model call; and a reranker returning one value for every candidate never
    /// discriminated at all, which reads as a clean flat result rather than the broken instrument it is
    /// (<see cref="CrossEncoderReranker.Audit"/> carries the same reasoning). Read all three before any
    /// latency here.</summary>
    private sealed record Row(
        string Arm, string Load,
        double WriteP50, double WriteP95, double WriteP99,
        double RecallP50, double RecallP95, double RecallP99,
        double WritesPerSecond, double RecallsPerSecond,
        int Errors, double HitRate, double SubjectsPerWrite, int DistinctRerankScores);

    /// <summary>Untimed writes that give a cell's fresh store <paramref name="count"/> entries BEFORE its
    /// timed region starts.
    ///
    /// <para><b>Why a cell needs this at all.</b> Without it, the mixed cell's early recalls raced an empty
    /// store: a recall that matches nothing skips verification and reranking entirely, so it is nearly free
    /// — and the mixed cell would look artificially FAST in exact proportion to how many of its own early
    /// recalls found nothing, for a reason that has nothing to do with contention. Seeding indices
    /// <c>[0, count)</c> — the SAME range every timed recall targets — means a recall always has something
    /// to match, in both cells, regardless of how far the concurrent timed WRITE loop (which uses an offset
    /// range so it does not just refresh a seeded row) has progressed.</para>
    ///
    /// <para><b>Resets <see cref="CountingAnnotation"/> before returning</b>, because annotation runs per
    /// write and a seed write is still a write: without this, the TIMED region's <c>SubjectsPerWrite</c>
    /// control would read the union of the seed's calls and its own. The reranker's audit needs no such
    /// reset — reranking fires on RECALL, and nothing here recalls.</para></summary>
    private static async Task SeedAsync(Rig rig, int count)
    {
        for (var i = 0; i < count; i++)
        {
            // Best-effort: a seed write that fails shows up as a lower HitRate in the timed region — the
            // control already built for exactly this — rather than aborting a cell partway through seeding.
            try { await rig.Engine.RememberAsync(new MemoryWrite("contention", Scope(i), Content(i))); }
            catch (Exception) { /* surfaced via HitRate, not here */ }
        }
        rig.Annotation.Reset();
    }

    /// <summary>One seam at a time. SEQUENTIAL on purpose — this is the baseline the mixed cell is read
    /// against, so contention in it would make the comparison meaningless (MemoryScaleSweep's own rule).
    /// </summary>
    private static async Task<Row> RunSoloAsync(Rig rig, int writes, int recalls)
    {
        await SeedAsync(rig, writes);

        var writeMs = new List<double>(writes);
        var errors = 0;

        var writeWall = Stopwatch.GetTimestamp();
        for (var i = 0; i < writes; i++)
        {
            var start = Stopwatch.GetTimestamp();
            // Offset past SeedAsync's [0, writes) range so a timed write INSERTS rather than refreshing a
            // seeded row — the store dedupes by (engine, task, scope, content hash).
            try
            {
                await rig.Engine.RememberAsync(new MemoryWrite("contention", Scope(writes + i), Content(writes + i)));
            }
            catch (Exception) { errors++; }
            writeMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        var writeSeconds = Stopwatch.GetElapsedTime(writeWall).TotalSeconds;

        var recallMs = new List<double>(recalls);
        var hits = 0;
        var recallWall = Stopwatch.GetTimestamp();
        for (var i = 0; i < recalls; i++)
        {
            var target = i * Math.Max(1, writes / Math.Max(1, recalls));
            var start = Stopwatch.GetTimestamp();
            try
            {
                var recall = await rig.Engine.RecallAsync(
                    new MemoryQuery("contention", Scope(target), Query(target), QueryLimit));
                if (recall.Items.Count > 0) hits++;
            }
            catch (Exception) { errors++; }
            recallMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        var recallSeconds = Stopwatch.GetElapsedTime(recallWall).TotalSeconds;

        var annotation = rig.Annotation.Audit;
        // Arm is blank here, not an oversight — this runner has no notion of which arm it ran under. RunAsync
        // is the caller that fills it in, now that a mixed row exists beside this one to distinguish it from.
        return new Row("", "solo",
            BenchStats.Percentile(writeMs, 0.50), BenchStats.Percentile(writeMs, 0.95),
            BenchStats.Percentile(writeMs, 0.99),
            BenchStats.Percentile(recallMs, 0.50), BenchStats.Percentile(recallMs, 0.95),
            BenchStats.Percentile(recallMs, 0.99),
            writeSeconds > 0 ? writes / writeSeconds : 0,
            recallSeconds > 0 ? recalls / recallSeconds : 0,
            errors, recalls > 0 ? hits / (double)recalls : 0,
            annotation.Calls > 0 ? annotation.Subjects / (double)annotation.Calls : 0,
            rig.Reranker.Audit.DistinctScores);
    }

    /// <summary>Writes and recalls driven CONCURRENTLY, which is the cell this sweep exists for.
    ///
    /// <para><b>Only two seams actually contend, and knowing which is the whole finding.</b> Router mode spawns
    /// one server process per model, so the embedder and the reranker are different processes and cannot
    /// contend with the judge at all. Annotation and judging both want the instruct model, so they share that
    /// one server's slots — every other pairing here is measured to CONFIRM it costs nothing.</para>
    ///
    /// <para><b>The write:recall ratio is printed, not implied.</b> Annotation runs per write and judging per
    /// recall, so an unbalanced driver reports the ratio rather than the contention.</para>
    ///
    /// <para><b><paramref name="workers"/> is PER LOOP, not the total in flight.</b> It sets
    /// <c>MaxDegreeOfParallelism</c> independently on the write loop and the recall loop below, so up to
    /// <c>2 × workers</c> requests reach the shared instruct-model server at once — that doubled figure, not
    /// <paramref name="workers"/> alone, is the number this cell actually prices. The <c>Load</c> label and
    /// the run preamble both say so explicitly rather than leaving a reader to infer it.</para>
    ///
    /// <para><b>Seeded to <paramref name="writes"/> entries, untimed, before either loop starts</b> — see
    /// <see cref="SeedAsync"/>. Without it an early recall here would race an empty store and be nearly free,
    /// pushing the contention delta toward zero for a reason that has nothing to do with contention.</para>
    /// </summary>
    private static async Task<Row> RunMixedAsync(Rig rig, int writes, int recalls, int workers)
    {
        await SeedAsync(rig, writes);

        var writeMs = new ConcurrentBag<double>();
        var recallMs = new ConcurrentBag<double>();
        var errors = 0;
        var hits = 0;

        var wall = Stopwatch.GetTimestamp();
        var writing = Parallel.ForEachAsync(Enumerable.Range(0, writes),
            new ParallelOptions { MaxDegreeOfParallelism = workers }, async (i, ct) =>
            {
                var start = Stopwatch.GetTimestamp();
                // Offset past SeedAsync's [0, writes) range so a timed write INSERTS rather than refreshing
                // a seeded row — the store dedupes by (engine, task, scope, content hash).
                try
                {
                    await rig.Engine.RememberAsync(
                        new MemoryWrite("contention", Scope(writes + i), Content(writes + i)), ct);
                }
                catch (Exception) { Interlocked.Increment(ref errors); }
                writeMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            });

        var recalling = Parallel.ForEachAsync(Enumerable.Range(0, recalls),
            new ParallelOptions { MaxDegreeOfParallelism = workers }, async (i, ct) =>
            {
                var target = i * Math.Max(1, writes / Math.Max(1, recalls));
                var start = Stopwatch.GetTimestamp();
                try
                {
                    var recall = await rig.Engine.RecallAsync(
                        new MemoryQuery("contention", Scope(target), Query(target), QueryLimit), ct);
                    if (recall.Items.Count > 0) Interlocked.Increment(ref hits);
                }
                catch (Exception) { Interlocked.Increment(ref errors); }
                recallMs.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            });

        await Task.WhenAll(writing, recalling);
        var seconds = Stopwatch.GetElapsedTime(wall).TotalSeconds;

        var write = writeMs.ToList();
        var read = recallMs.ToList();
        var annotation = rig.Annotation.Audit;
        // "/loop": workers is set independently on the write and recall loops above, so up to 2x this many
        // requests are actually in flight against the shared server — the number that matters for contention.
        return new Row("", $"mixed×{workers}/loop",
            BenchStats.Percentile(write, 0.50), BenchStats.Percentile(write, 0.95),
            BenchStats.Percentile(write, 0.99),
            BenchStats.Percentile(read, 0.50), BenchStats.Percentile(read, 0.95),
            BenchStats.Percentile(read, 0.99),
            seconds > 0 ? writes / seconds : 0, seconds > 0 ? recalls / seconds : 0,
            errors, recalls > 0 ? hits / (double)recalls : 0,
            annotation.Calls > 0 ? annotation.Subjects / (double)annotation.Calls : 0,
            rig.Reranker.Audit.DistinctScores);
    }

    /// <summary>Mixed against solo, refusing a negative exactly as <see cref="MemoryScaleSweep"/> does.
    /// <para>Concurrency cannot make a seam FASTER than running it alone, so a negative delta is a statement
    /// about run-to-run variance rather than about contention — and one run per cell carries no variance
    /// estimate to net it against. Printing it as a percentage anyway is how "−7% of the p50" enters a
    /// document as a finding.</para></summary>
    private static void PrintContentionCost(Row solo, Row mixed)
    {
        var delta = mixed.RecallP50 - solo.RecallP50;
        if (delta <= 0)
        {
            Console.WriteLine($"    {mixed.Arm}: NOT READABLE — mixed measured {-delta:F1}ms FASTER than solo, "
                + "which contention cannot do. One run per cell, so this is variance. Re-run with --repeat.");
            return;
        }
        Console.WriteLine($"    {mixed.Arm}: contention costs {delta:F1}ms of the p50 recall "
            + $"({delta / solo.RecallP50 * 100:F0}% of it)");
    }

    private static void PrintNotSwept()
    {
        Console.WriteLine("\nNOT swept (stated rather than left implicit):");
        Console.WriteLine("  - RECALL QUALITY. No ground truth here. Nothing says whether contention changes");
        Console.WriteLine("    WHAT is returned, only what it costs.");
        Console.WriteLine("  - A BUSY GPU, and this is the figure that does NOT transfer. Every cell was taken");
        Console.WriteLine("    with the device quiet; generation swings from 12x faster to 26x slower between a");
        Console.WriteLine("    quiet and a contended device while encoding stays ahead throughout. Read every");
        Console.WriteLine("    number here as the quiet-device case (pitfalls.md, docs/model-tasks.md section 3).");
        Console.WriteLine("  - `--parallel`. The chat server's slot count is fixed; varying it is the follow-on,");
        Console.WriteLine("    and the axis ProviderAdmission speaks to.");
        Console.WriteLine("  - POSTGRES. SQLite only, matching every sweep here.");
        Console.WriteLine("  - Absolute latencies are THIS MACHINE's. Compare the arm differences.");
    }

    private const int QueryLimit = 10;
    private static string Scope(int i) => $"s{i % 20}";
    private static string Content(int i) =>
        $"entry marker{i} covers the deployment checklist and its approval step for component {i % 97}";
    private static string Query(int i) => $"marker{i}";

    /// <summary>Writes and recalls per cell. <c>--smoke</c> collapses them so the harness can be exercised end
    /// to end in under a minute — the same reason <see cref="MemoryScaleSweep"/> carries <c>--sizes</c>: an
    /// instrument is code nothing else validates, and one whose only run takes an hour gets validated by
    /// reading, which is how a corpus arm once reported 0.0000 on every shape and looked like a result.
    /// </summary>
    private static (int Writes, int Recalls) ParseVolume(string[] args) =>
        args.Contains("--smoke") ? (8, 8) : (ParseInt(args, "--writes", 100), ParseInt(args, "--recalls", 100));

    /// <summary>A named integer flag, e.g. <c>--writes 500</c> overriding <paramref name="fallback"/>. Mirrors
    /// <see cref="MemoryScaleSweep"/>'s own flag parsers (<c>ParseRepeat</c>, <c>ParseWarmup</c>): same shape,
    /// same reason.</summary>
    private static int ParseInt(string[] args, string flag, int fallback)
    {
        var i = Array.IndexOf(args, flag);
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0 ? n : fallback;
    }

    /// <summary>The topology label this run stamps on every row, <c>--arm dedicated</c> overriding the
    /// default. The orchestrator restarts this process once per topology (dedicated / router-resident /
    /// router-swapping) against different <c>LYNTAI_LIVE_*</c> endpoints, and nothing in this process
    /// otherwise knows which one it is — without this flag, three separate runs would print
    /// identically-labelled rows once combined into one table. Defaults to "unlabelled" so a bare invocation
    /// (this file's own refusal-path check) still prints a readable row rather than a blank label.</summary>
    private static string ParseArm(string[] args)
    {
        var i = Array.IndexOf(args, "--arm");
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : "unlabelled";
    }

    /// <summary>How many times to run each cell, <c>--repeat 3</c> overriding the default 1. Mirrors
    /// <see cref="MemoryScaleSweep"/>'s own <c>ParseRepeat</c> for the reason it recorded: a p99 under
    /// contention is the noisiest number in that file, and a single-repeat run of one comparison reported a
    /// 4-5 point improvement that vanished at five repeats — this sweep's mixed cell is exactly that kind of
    /// contended measurement, read here off <c>RecallP50</c> rather than <c>P99</c>.</summary>
    private static int ParseRepeat(string[] args)
    {
        var i = Array.IndexOf(args, "--repeat");
        return i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var n) && n > 0 ? n : 1;
    }

    /// <summary>Runs one cell <paramref name="repeat"/> times, each against its OWN fresh <see cref="Rig"/> —
    /// a shared store across "independent" repetitions would let the second warm from the first, and a
    /// shared <see cref="CountingAnnotation"/> or <see cref="CrossEncoderReranker"/> would accumulate their
    /// counters across runs instead of reporting what THIS repetition alone did
    /// (<see cref="MemoryScaleSweep"/>'s own reason for a fresh store per repetition). Keeps the MEDIAN by
    /// <see cref="Row.RecallP50"/> — the statistic <see cref="PrintContentionCost"/> reads — because a mean
    /// lets one slow run (a background build, a checkpoint) carry the cell. Returns a <c>null</c> median on
    /// a mid-run refusal, so the caller can still exit 1 rather than reporting an unmeasured repetition as a
    /// result.</summary>
    private static async Task<(Row? Median, List<Row> Runs)> RunRepeatedCellAsync(
        HttpClient http, int repeat, Func<Rig, Task<Row>> runCell)
    {
        var runs = new List<Row>(repeat);
        for (var i = 0; i < repeat; i++)
        {
            var rig = await BuildRigAsync(http);
            if (rig is null) return (null, runs);
            using var db = rig.Db;
            runs.Add(await runCell(rig));
        }
        return (runs.OrderBy(r => r.RecallP50).ElementAt(runs.Count / 2), runs);
    }

    /// <summary>The spread across <paramref name="runs"/>, printed only once <paramref name="repeat"/>
    /// exceeds 1 — mirrors <see cref="MemoryScaleSweep"/>'s own repeat-spread line so a reader can see
    /// whether a difference between cells outruns run-to-run noise.</summary>
    private static void PrintSpread(List<Row> runs, int repeat)
    {
        if (repeat <= 1) return;
        Console.WriteLine($"      ({repeat} runs, p50 recall spread " +
            $"{runs.Min(r => r.RecallP50):F1}–{runs.Max(r => r.RecallP50):F1}ms)");
    }

    private static void PrintRow(Row r)
    {
        Console.WriteLine($"  {r.Arm}/{r.Load,-8} write p50 {r.WriteP50:F1}ms p95 {r.WriteP95:F1}ms " +
            $"p99 {r.WriteP99:F1}ms ({r.WritesPerSecond:F2}/s) · recall p50 {r.RecallP50:F1}ms " +
            $"p95 {r.RecallP95:F1}ms p99 {r.RecallP99:F1}ms ({r.RecallsPerSecond:F2}/s) · errors {r.Errors}");
        Console.WriteLine($"  {"",-8} controls: hit-rate {r.HitRate:F3}" +
            (r.HitRate < 1 ? "  ← BELOW 1: partly timing MISSES, not recalls" : "") +
            $" · subjects/write {r.SubjectsPerWrite:F2}" +
            (r.SubjectsPerWrite <= 0 ? "  ← ZERO: annotation is a no-op that still cost a call" : "") +
            $" · distinct rerank scores {r.DistinctRerankScores}" +
            (r.DistinctRerankScores <= 1 ? "  ← <=1: the reranker never discriminated" : ""));
    }

    public static async Task<int> RunAsync(string[] args)
    {
        using var http = NewHttp();

        // Built only to gate on reachability before anything prints — the positive control BuildRigAsync's
        // own header describes. Discarded rather than reused: every cell run below (RunRepeatedCellAsync)
        // builds its OWN fresh rig, and reusing this one would let repetition 0 of the solo cell warm from it.
        var probe = await BuildRigAsync(http);
        if (probe is null) return 1;
        probe.Db.Dispose();

        Console.WriteLine("memory-contention: chat, reranker, embedder and the shipped annotation/" +
            "verification policies are all wired onto one engine.");

        var (writes, recalls) = ParseVolume(args);
        var workers = ParseInt(args, "--workers", 4);
        var repeat = ParseRepeat(args);
        var arm = ParseArm(args);
        // The write:recall ratio is PRINTED, not implied — annotation runs per write and judging per recall,
        // so an unbalanced driver would report the ratio rather than the contention (RunMixedAsync's own doc).
        // `workers` is likewise spelled out as PER LOOP: the mixed cell drives it independently on the write
        // and recall loops, so up to 2x this many requests are actually in flight at once.
        Console.WriteLine($"  {writes} writes, {recalls} recalls per cell — write:recall ratio {writes}:{recalls}, "
            + $"{workers} workers per loop on the mixed cell (up to {workers * 2} requests in flight together), "
            + $"repeat {repeat}");
        // Both cells seed `writes` entries UNTIMED before their clock starts (SeedAsync's own doc says why:
        // otherwise an empty-store recall is nearly free, which would understate contention). That is a real
        // model call per seed write, so each cell's WALL time exceeds what its own timed numbers add up to.
        Console.WriteLine($"  both cells seed {writes} entries UNTIMED first, so recalls never race an empty "
            + "store — wall time therefore exceeds the timed numbers below\n");

        var (soloMedian, soloRuns) = await RunRepeatedCellAsync(http, repeat, r => RunSoloAsync(r, writes, recalls));
        if (soloMedian is null) return 1;
        var solo = soloMedian with { Arm = arm };
        PrintRow(solo);
        PrintSpread(soloRuns, repeat);

        var (mixedMedian, mixedRuns) =
            await RunRepeatedCellAsync(http, repeat, r => RunMixedAsync(r, writes, recalls, workers));
        if (mixedMedian is null) return 1;
        var mixed = mixedMedian with { Arm = arm };
        PrintRow(mixed);
        PrintSpread(mixedRuns, repeat);

        PrintContentionCost(solo, mixed);
        PrintNotSwept();
        return 0;
    }
}
