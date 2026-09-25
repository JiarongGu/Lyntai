using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// An annotator's own TIMEOUT must not fail the WRITE. The seam is documented best-effort — the engine logs
/// and stores without subjects — but it rethrew <see cref="OperationCanceledException"/> first, and an
/// <see cref="HttpClient"/> timeout surfaces as <see cref="TaskCanceledException"/>, which IS one. So the
/// likeliest failure a model-backed policy has took the whole <c>RememberAsync</c> down with it.
///
/// <para>The twin of <c>MemoryVerificationTimeoutTests</c>, which pins the same rule on the READ path after
/// a bench run lost 40 minutes of ingestion to it (2026-09-09, <c>docs/FIXES.md</c>). This half was filed
/// rather than assumed, because the two seams differ in what a failure costs: a recall that degrades returns
/// the same items, while a write that degrades stores the entry with no subject edges — a permanent
/// deficiency for that one entry, and still cheaper than losing the fact.</para>
/// </summary>
public class MemoryAnnotationTimeoutTests
{
    private sealed class TimesOut : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(
            MemoryAnnotationRequest request, CancellationToken ct = default) =>
            // exactly what HttpClient throws on its own timeout: a cancellation nobody asked for
            throw new TaskCanceledException("the request was canceled due to the configured HttpClient.Timeout");
    }

    /// <summary>Throws the CALLER's cancellation, MARKED — which is what makes the caller-cancel test able
    /// to discriminate. <c>RememberAsync</c> reaches <c>store.UpsertAsync</c> under no try at all, and the
    /// in-memory store throws its own <see cref="OperationCanceledException"/> there, so a bare
    /// <c>ThrowsAnyAsync</c> passes whatever this seam does — including under the wrong fix. Asserting on
    /// the marker is what tells the annotator's cancellation from the store's.</summary>
    private sealed class CancelsWithMarker : IMemoryAnnotationPolicy
    {
        public const string Marker = "the annotator saw the caller's cancellation";

        public Task<MemoryAnnotation> AnnotateAsync(
            MemoryAnnotationRequest request, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) throw new OperationCanceledException(Marker, ct);
            return Task.FromResult(MemoryAnnotation.None);
        }
    }

    /// <summary>Both context reads OFF, so the annotator is the FIRST thing in the write path to observe the
    /// token. With the shipped defaults the store's own <c>SeedAsync</c> throws before the seam is reached,
    /// and the test would be measuring the store.</summary>
    private static GraphMemoryOptions NoContextReads() =>
        new() { AnnotationContext = 0, AnnotationKnownSubjects = 0 };

    private static async Task<MemoryRef> RememberWithTimingOutAnnotator(CancellationToken ct = default)
    {
        var engine = new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                Annotation = new TimesOut(),
            });
        return (await engine.RememberAsync(new MemoryWrite("t", "s", "marker11 the deployment checklist"), ct))
            .Reference;
    }

    [Fact]
    public async Task An_annotator_timing_out_stores_the_fact_rather_than_failing_the_write()
    {
        var stored = await RememberWithTimingOutAnnotator();

        // MemoryRef is a value type, so its own existence asserts nothing — the id is what proves a row was
        // written rather than a default struct handed back.
        Assert.NotEmpty(stored.Id);
        Assert.Equal("graph", stored.Engine);
    }

    [Fact]
    public async Task The_fact_it_stored_is_recallable()
    {
        // The premise of the test above: an id proves the write path ran, not that the entry is findable.
        var engine = new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                Annotation = new TimesOut(),
            });
        await engine.RememberAsync(new MemoryWrite("t", "s", "marker11 the deployment checklist"));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "marker11", 10));

        Assert.NotEmpty(recall.Items);
    }

    [Fact]
    public async Task A_CALLER_cancelling_still_propagates_FROM_THE_ANNOTATOR()
    {
        // The other half, or the fix would be "swallow every cancellation" — which would let a cancelled
        // write complete. The marker is load-bearing: see CancelsWithMarker for why a bare ThrowsAnyAsync
        // cannot fail here.
        var engine = new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore(), NoContextReads(), seams: new GraphMemorySeams
            {
                Annotation = new CancelsWithMarker(),
            });
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RememberAsync(new MemoryWrite("t", "s", "marker11 the checklist"), cts.Token));

        Assert.Equal(CancelsWithMarker.Marker, thrown.Message);
    }
}
