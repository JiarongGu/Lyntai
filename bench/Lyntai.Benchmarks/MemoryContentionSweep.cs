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
/// One backend serving the THREE model-backed memory seams — annotation, verification, embedding — wired
/// onto a single <see cref="GraphMemoryEngine"/>, so a cell prices what CONTENTION between them costs rather
/// than any one seam's own accuracy. <c>memory-verification</c>, <c>memory-annotation</c> and
/// <c>memory-enrichment</c> each measure their own seam in isolation; nothing had put all three in front of
/// one server before, and <c>memory-scale</c> named the gap outright in its own NOT-swept list.
///
/// <para><b>Verification is a SINGULAR slot, not a fourth seam (D115).</b>
/// <see cref="LlmMemoryVerificationPolicy"/> and <see cref="CrossEncoderVerifier"/> are ALTERNATIVES for
/// <c>IMemoryVerificationPolicy</c>, never simultaneous, and the pick decides whether contention exists at
/// all — see <see cref="PrintBackendComparison"/>.</para>
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
    /// as zero.</para>
    ///
    /// <para><b><see cref="Reranker"/> is built regardless of which backend fills verification.</b> Under
    /// <see cref="Verifier.Rerank"/> it scores every candidate through <see cref="CrossEncoderVerifier"/>;
    /// under <see cref="Verifier.Judge"/> it is still probed for reachability but never otherwise invoked —
    /// and <see cref="CrossEncoderReranker.Reset"/>, called right after that probe succeeds, wipes the
    /// probe's own scores so they cannot leak into the count. Reading <c>DistinctRerankScores</c> as 0 under
    /// <c>judge</c> and non-zero under <c>rerank</c> is exactly the differential this sweep exists to show.
    /// </para></summary>
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

    /// <summary>Which implementation fills the engine's single verification slot.
    ///
    /// <para><b>They are alternatives, not additions.</b> <c>IMemoryVerificationPolicy</c> is singular, so a
    /// deployment picks one — and the pick decides whether any two seams share a model at all. Under
    /// <see cref="Judge"/> annotation and verification both run on the instruct model and contend; under
    /// <see cref="Rerank"/> verification moves to its own cross-encoder and nothing is shared.</para></summary>
    private enum Verifier { Judge, Rerank }

    private static async Task<Rig?> BuildRigAsync(HttpClient http, Verifier verifier)
    {
        var chat = await SweepDoubles.TryRealChatAsync(http, "memory-contention");
        if (chat is null) return null;                      // refuses; the reason is already on stderr

        // Checked regardless of `verifier`: under Judge this is still the positive control the class doc
        // describes (an unreachable reranker must not silently look like a fast, unused one); under Rerank
        // it is the seam actually being priced.
        var reranker = new CrossEncoderReranker(http, CrossEncoderReranker.BaseUrl, CrossEncoderReranker.Model);
        if (!await reranker.ReachableAsync())
        {
            Console.Error.WriteLine("memory-contention: no reranker — this arm prices a seam, not a stand-in.");
            return null;
        }
        // The probe above just scored two sentences on THIS instance — without wiping that, DistinctRerankScores
        // would start every cell already polluted by setup, the same contamination SeedAsync's writes once left
        // in CountingAnnotation (pitfalls.md).
        reranker.Reset();

        var embedder = new SweepDoubles.OpenAiCompatibleEmbedder(http, SweepDoubles.BaseUrl, SweepDoubles.Model);
        var clients = new SweepDoubles.BenchClientFactory(chat);

        // The SHIPPED policies, not bench-local ones — an arm has to exercise the shipped prompt, parsing and
        // fail-open behaviour or it measures something no consumer inherits (CrossEncoderRerank's own reasoning).
        var annotation = new CountingAnnotation(new LlmMemoryAnnotationPolicy(clients));
        // IMemoryVerificationPolicy is SINGULAR (D115): the two backends fill the same slot, never both at
        // once. `top: QueryLimit` mirrors MemoryLocomoBench's own call site — a fixed endorsement count keeps
        // the promoted set no larger than the page.
        IMemoryVerificationPolicy verification = verifier == Verifier.Rerank
            ? new CrossEncoderVerifier(reranker, QueryLimit)
            : new LlmMemoryVerificationPolicy(clients);

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
    /// <para><b>What contends is decided by <see cref="Verifier"/>, not fixed.</b> The embedder is always its
    /// own process, so it can never contend with anything here. Under <see cref="Verifier.Judge"/> annotation
    /// and verification both want the instruct model and share its slots — that sharing is the contention
    /// this cell exists to price. Under <see cref="Verifier.Rerank"/> verification moves to the cross-encoder's
    /// own process, so nothing shares a model at all — this cell is the null-result control
    /// <see cref="PrintBackendComparison"/> reads against.</para>
    ///
    /// <para><b>The write:recall ratio is printed, not implied.</b> Annotation runs per write and verification
    /// per recall, so an unbalanced driver reports the ratio rather than the contention.</para>
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

    /// <summary>The observed min/max of <see cref="Row.RecallP50"/> across <paramref name="runs"/> — the
    /// SAME two numbers <see cref="PrintSpread"/> already prints, factored out so a second caller
    /// (<see cref="PrintContentionCost"/>'s unresolved-delta branch) reads the identical values rather than
    /// recomputing them and risking the two disagreeing.</summary>
    private static (double Min, double Max) RecallP50Range(List<Row> runs) =>
        (runs.Min(r => r.RecallP50), runs.Max(r => r.RecallP50));

    /// <summary>Mixed against solo, refusing a negative exactly as <see cref="MemoryScaleSweep"/> does.
    ///
    /// <para><b>What a negative delta MEANS depends on <paramref name="repeat"/>, and the wording must say
    /// which case it is.</b> At <c>repeat == 1</c> there is no variance estimate at all — "one run per cell"
    /// is true, and "re-run with --repeat" is real advice. At <c>repeat &gt; 1</c> that advice is FALSE: the
    /// repeats already ran, so telling a reader who used <c>--repeat 3</c> to re-run with it wastes their
    /// time and a record quoting the line publishes a false claim about its own configuration. The honest
    /// read there is a RESULT, not a failure to measure: if the two cells' OWN spreads overlap, contention
    /// is at or below what this instrument resolves at this repeat count for this arm — not zero, not
    /// negative, unresolved. Both spreads are printed on the line so a reader sees the overlap rather than
    /// taking it on trust.</para></summary>
    private static void PrintContentionCost(Row solo, Row mixed, int repeat, List<Row> soloRuns, List<Row> mixedRuns)
    {
        var delta = mixed.RecallP50 - solo.RecallP50;
        if (delta > 0)
        {
            Console.WriteLine($"    {mixed.Arm}: contention costs {delta:F1}ms of the p50 recall "
                + $"({delta / solo.RecallP50 * 100:F0}% of it)");
            return;
        }
        if (repeat <= 1)
        {
            Console.WriteLine($"    {mixed.Arm}: NOT READABLE — mixed measured {-delta:F1}ms FASTER than solo, "
                + "which contention cannot do. One run per cell, so this is variance. Re-run with --repeat.");
            return;
        }
        var (soloMin, soloMax) = RecallP50Range(soloRuns);
        var (mixedMin, mixedMax) = RecallP50Range(mixedRuns);
        // Overlap is checked, not assumed: a genuinely negative delta that survives `repeat` runs with
        // NON-overlapping spreads is not explained by noise and should not be worded as if it were.
        if (soloMin <= mixedMax && mixedMin <= soloMax)
        {
            Console.WriteLine($"    {mixed.Arm}: UNRESOLVED at {repeat} runs — solo {soloMin:F1}-{soloMax:F1}ms "
                + $"and mixed {mixedMin:F1}-{mixedMax:F1}ms OVERLAP, so the {-delta:F1}ms the medians differ "
                + "by is inside this instrument's noise floor for this arm: contention is not shown to be "
                + "zero, negative, or any particular size — only that it did not resolve at this repeat count.");
            return;
        }
        Console.WriteLine($"    {mixed.Arm}: STILL NOT READABLE at {repeat} runs — mixed's spread "
            + $"({mixedMin:F1}-{mixedMax:F1}ms) sits entirely BELOW solo's ({soloMin:F1}-{soloMax:F1}ms), "
            + "which contention cannot do even accounting for the measured spread. Not explained by "
            + "run-to-run variance at this repeat count.");
    }

    /// <summary>What moving verification off the shared instruct model buys — same unresolved-vs-not-yet-
    /// measured distinction as <see cref="PrintContentionCost"/>, and for the identical reason: each side is
    /// itself a MEDIAN over <paramref name="repeat"/> runs once <c>--repeat</c> is used, so "one run per
    /// cell" is equally false here when it is.</summary>
    private static void PrintBackendComparison(Row judgeMixed, Row rerankMixed, int repeat,
        List<Row> judgeMixedRuns, List<Row> rerankMixedRuns)
    {
        var delta = judgeMixed.RecallP50 - rerankMixed.RecallP50;
        if (delta > 0)
        {
            Console.WriteLine($"\n  judge vs rerank: moving verification off the shared instruct model buys "
                + $"{delta:F1}ms of the mixed p50 recall ({delta / judgeMixed.RecallP50 * 100:F0}% of it)");
            return;
        }
        if (repeat <= 1)
        {
            Console.WriteLine($"\n  judge vs rerank: NOT READABLE — judge measured {-delta:F1}ms FASTER than "
                + "rerank at the mixed cell, though only judge shares a model with annotation. One run per "
                + "cell, so this is variance. Re-run with --repeat.");
            return;
        }
        var (judgeMin, judgeMax) = RecallP50Range(judgeMixedRuns);
        var (rerankMin, rerankMax) = RecallP50Range(rerankMixedRuns);
        if (judgeMin <= rerankMax && rerankMin <= judgeMax)
        {
            Console.WriteLine($"\n  judge vs rerank: UNRESOLVED at {repeat} runs — judge's mixed spread "
                + $"{judgeMin:F1}-{judgeMax:F1}ms and rerank's {rerankMin:F1}-{rerankMax:F1}ms OVERLAP, so the "
                + $"{-delta:F1}ms the medians differ by is inside this instrument's noise floor: the backend "
                + "choice is not shown to make the mixed cell faster OR slower at this repeat count.");
            return;
        }
        Console.WriteLine($"\n  judge vs rerank: STILL NOT READABLE at {repeat} runs — judge's mixed spread "
            + $"({judgeMin:F1}-{judgeMax:F1}ms) sits entirely BELOW rerank's ({rerankMin:F1}-{rerankMax:F1}ms), "
            + "though only judge shares a model with annotation. Not explained by run-to-run variance at "
            + "this repeat count.");
    }

    /// <summary>The device-contention LABEL the orchestrator (`devtools/scripts/memory-contention.mjs`)
    /// sets via <c>LYNTAI_CONTENTION_DEVICE</c> before invoking this sweep — <c>"busy"</c> when it is
    /// holding a busy-device load for the cell's duration, <c>"quiet"</c> otherwise (the default, so a
    /// bare <c>dotnet run -- --contention</c> outside the orchestrator still describes itself correctly).
    /// LABELLING ONLY: nothing here reads this value to change what is measured, only what is PRINTED about
    /// it — the orchestrator alone decides whether a load actually runs.</summary>
    private static string DeviceMode() =>
        Environment.GetEnvironmentVariable("LYNTAI_CONTENTION_DEVICE") is { Length: > 0 } v ? v : "quiet";

    private static void PrintNotSwept(string device)
    {
        Console.WriteLine("\nNOT swept (stated rather than left implicit):");
        Console.WriteLine("  - RECALL QUALITY. No ground truth here. Nothing says whether contention changes");
        Console.WriteLine("    WHAT is returned, only what it costs.");
        Console.WriteLine("  - JUDGE AND RERANK RUNNING TOGETHER. IMemoryVerificationPolicy is a SINGULAR");
        Console.WriteLine("    slot (D115): a deployment fills it with exactly one backend, so the two can");
        Console.WriteLine("    never contend with EACH OTHER — only annotation can contend, and only with");
        Console.WriteLine("    whichever one is loaded.");
        if (device == "busy")
        {
            Console.WriteLine("  - NOT excluded THIS run: the device was DELIBERATELY loaded"
                + " (LYNTAI_CONTENTION_DEVICE=busy) by a second llama-server generating tokens on its own");
            Console.WriteLine("    port, held by the orchestrator for the whole cell. Every number above is");
            Console.WriteLine("    the CONTENDED case, not the quiet one — do not read it as a quiet-device");
            Console.WriteLine("    baseline (pitfalls.md, docs/model-tasks.md section 3).");
        }
        else
        {
            Console.WriteLine("  - A BUSY GPU, and this is the figure that does NOT transfer. Every cell was taken");
            Console.WriteLine("    with the device quiet; generation swings from 12x faster to 26x slower between a");
            Console.WriteLine("    quiet and a contended device while encoding stays ahead throughout. Read every");
            Console.WriteLine("    number here as the quiet-device case (pitfalls.md, docs/model-tasks.md section 3).");
        }
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

    /// <summary>Which verification backend(s) to run, from <c>--verifier judge|rerank|both</c> — default
    /// <c>both</c>, since <see cref="PrintBackendComparison"/> needs a mixed row from each side to say
    /// anything. An unrecognised value falls back to <c>both</c> rather than refusing, matching every other
    /// flag parser here.</summary>
    private static Verifier[] ParseVerifiers(string[] args)
    {
        var i = Array.IndexOf(args, "--verifier");
        var value = i >= 0 && i + 1 < args.Length ? args[i + 1] : "both";
        return value switch
        {
            "judge" => [Verifier.Judge],
            "rerank" => [Verifier.Rerank],
            _ => [Verifier.Judge, Verifier.Rerank],
        };
    }

    private static string VerifierName(Verifier verifier) => verifier == Verifier.Rerank ? "rerank" : "judge";

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
        HttpClient http, int repeat, Verifier verifier, Func<Rig, Task<Row>> runCell)
    {
        var runs = new List<Row>(repeat);
        for (var i = 0; i < repeat; i++)
        {
            var rig = await BuildRigAsync(http, verifier);
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
        var (min, max) = RecallP50Range(runs);
        Console.WriteLine($"      ({repeat} runs, p50 recall spread {min:F1}–{max:F1}ms)");
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
            (r.DistinctRerankScores > 1 ? ""
                : r.Arm == "judge" ? "  (0 expected under judge — verification never calls the reranker)"
                : "  ← <=1: the reranker never discriminated"));
    }

    public static async Task<int> RunAsync(string[] args)
    {
        using var http = NewHttp();

        var verifiers = ParseVerifiers(args);

        // Built only to gate on reachability before anything prints — the positive control BuildRigAsync's
        // own header describes. Discarded rather than reused: every cell run below (RunRepeatedCellAsync)
        // builds its OWN fresh rig, and reusing this one would let repetition 0 of the solo cell warm from it.
        // Either requested backend gates the SAME two models (chat, reranker), so which one is passed here
        // does not change what is checked.
        var probe = await BuildRigAsync(http, verifiers[0]);
        if (probe is null) return 1;
        probe.Db.Dispose();

        Console.WriteLine("memory-contention: three model-backed seams — annotation, verification, embedding "
            + "— wired onto one engine. Verification is a SINGULAR slot (D115): this run fills it with each "
            + "requested backend in turn, never both at once.");

        var device = DeviceMode();
        Console.WriteLine(device == "busy"
            ? "  device: BUSY — LYNTAI_CONTENTION_DEVICE=busy, the orchestrator is holding a busy-device load "
                + "for this cell. Every figure below is the CONTENDED case."
            : "  device: quiet (LYNTAI_CONTENTION_DEVICE unset or \"quiet\")");

        var (writes, recalls) = ParseVolume(args);
        var workers = ParseInt(args, "--workers", 4);
        var repeat = ParseRepeat(args);
        // The write:recall ratio is PRINTED, not implied — annotation runs per write and verification per
        // recall, so an unbalanced driver would report the ratio rather than the contention (RunMixedAsync's
        // own doc). `workers` is likewise spelled out as PER LOOP: the mixed cell drives it independently on
        // the write and recall loops, so up to 2x this many requests are actually in flight at once.
        Console.WriteLine($"  {writes} writes, {recalls} recalls per cell — write:recall ratio {writes}:{recalls}, "
            + $"{workers} workers per loop on the mixed cell (up to {workers * 2} requests in flight together), "
            + $"repeat {repeat}");
        // Both cells seed `writes` entries UNTIMED before their clock starts (SeedAsync's own doc says why:
        // otherwise an empty-store recall is nearly free, which would understate contention). That is a real
        // model call per seed write, so each cell's WALL time exceeds what its own timed numbers add up to.
        Console.WriteLine($"  both cells seed {writes} entries UNTIMED first, so recalls never race an empty "
            + "store — wall time therefore exceeds the timed numbers below");

        var mixedByVerifier = new Dictionary<Verifier, Row>();
        var mixedRunsByVerifier = new Dictionary<Verifier, List<Row>>();
        foreach (var verifier in verifiers)
        {
            var name = VerifierName(verifier);
            Console.WriteLine($"\n-- verifier: {name} --");

            var (soloMedian, soloRuns) =
                await RunRepeatedCellAsync(http, repeat, verifier, r => RunSoloAsync(r, writes, recalls));
            if (soloMedian is null) return 1;
            var solo = soloMedian with { Arm = name };
            PrintRow(solo);
            PrintSpread(soloRuns, repeat);

            var (mixedMedian, mixedRuns) =
                await RunRepeatedCellAsync(http, repeat, verifier, r => RunMixedAsync(r, writes, recalls, workers));
            if (mixedMedian is null) return 1;
            var mixed = mixedMedian with { Arm = name };
            PrintRow(mixed);
            PrintSpread(mixedRuns, repeat);

            PrintContentionCost(solo, mixed, repeat, soloRuns, mixedRuns);
            mixedByVerifier[verifier] = mixed;
            mixedRunsByVerifier[verifier] = mixedRuns;
        }

        // Only readable once BOTH backends ran — a single `--verifier judge` or `--verifier rerank` invocation
        // has nothing to compare, the same reason PrintSpread stays silent below repeat 2.
        if (mixedByVerifier.TryGetValue(Verifier.Judge, out var judgeMixed) &&
            mixedByVerifier.TryGetValue(Verifier.Rerank, out var rerankMixed))
            PrintBackendComparison(judgeMixed, rerankMixed, repeat,
                mixedRunsByVerifier[Verifier.Judge], mixedRunsByVerifier[Verifier.Rerank]);

        PrintNotSwept(device);
        return 0;
    }
}
