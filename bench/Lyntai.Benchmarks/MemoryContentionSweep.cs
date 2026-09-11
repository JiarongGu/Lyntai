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
/// <para><b>Refuses rather than substituting, on every seam.</b> <see cref="BuildRigAsync"/> checks the chat
/// model and the reranker before returning a <see cref="Rig"/>, the same posture
/// <see cref="SweepDoubles.TryRealChatAsync"/> and <see cref="SweepDoubles.TryRealEmbedderAsync"/> already
/// take: an arm that silently ran without a model would look exactly like a fast one, and a bag-of-words
/// stand-in for the embedder was withdrawn once already for producing exactly that illusion (TASKS.md Part
/// 69). This is the positive control every cell here depends on.</para>
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

    public static async Task<int> RunAsync(string[] args)
    {
        using var http = NewHttp();
        var rig = await BuildRigAsync(http);
        if (rig is null) return 1;

        using var db = rig.Db;
        Console.WriteLine("memory-contention: chat, reranker, embedder and the shipped annotation/" +
            "verification policies are all wired onto one engine.");
        return 0;
    }
}
