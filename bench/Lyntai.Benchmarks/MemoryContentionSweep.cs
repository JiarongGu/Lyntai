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

    /// <summary>One seam at a time. SEQUENTIAL on purpose — this is the baseline the mixed cell is read
    /// against, so contention in it would make the comparison meaningless (MemoryScaleSweep's own rule).
    /// </summary>
    private static async Task<Row> RunSoloAsync(Rig rig, int writes, int recalls)
    {
        var writeMs = new List<double>(writes);
        var errors = 0;

        var writeWall = Stopwatch.GetTimestamp();
        for (var i = 0; i < writes; i++)
        {
            var start = Stopwatch.GetTimestamp();
            try { await rig.Engine.RememberAsync(new MemoryWrite("contention", Scope(i), Content(i))); }
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
        // Arm is blank here on purpose, not an oversight — this runner has no notion of which arm it ran
        // under. The caller fills it in once there is more than one arm to distinguish.
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

    private static void PrintRow(Row r)
    {
        Console.WriteLine($"  {r.Load,-8} write p50 {r.WriteP50:F1}ms p95 {r.WriteP95:F1}ms " +
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
        var rig = await BuildRigAsync(http);
        if (rig is null) return 1;

        using var db = rig.Db;
        Console.WriteLine("memory-contention: chat, reranker, embedder and the shipped annotation/" +
            "verification policies are all wired onto one engine.");

        var (writes, recalls) = ParseVolume(args);
        Console.WriteLine($"  {writes} writes, {recalls} recalls per cell\n");

        var solo = await RunSoloAsync(rig, writes, recalls);
        PrintRow(solo);
        return 0;
    }
}
