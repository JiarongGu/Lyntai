using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>
/// Every fail-open seam in the memory subsystem degrades on a component's OWN deadline, and still propagates
/// the CALLER's cancellation. The two are told apart by <c>ct.IsCancellationRequested</c> rather than by the
/// exception's type, because an <c>HttpClient</c> timeout arrives as <see cref="TaskCanceledException"/> —
/// which IS an <see cref="OperationCanceledException"/> and says nothing about the caller.
///
/// <para><b>Why one suite rather than a fact per file: these handlers are in SERIES.</b> A single timeout
/// from a BYO embedder passes through the seed source, the graph engine's gather, the composite, and then
/// the walk or the composition — four nested fail-open handlers, and a bare rethrow at ANY of them breaks
/// the promise of ALL of them. Fixing one link and testing it in isolation would have looked green while the
/// chain still leaked, which is why <see cref="A_timeout_deep_in_the_chain_degrades_at_every_layer_above_it"/>
/// exists alongside the per-layer facts.</para>
///
/// <para>Found 2026-09-09 by asking whether the annotation seam shared the verification seam's defect
/// (<c>docs/FIXES.md</c>). It did, and so did every other fail-open handler here.</para>
/// </summary>
public class MemoryFailOpenCancellationTests
{
    private static MemoryQuery Query(string q = "marker") => new("t", "s", q, 10);

    private static MemoryWrite Write(string content = "marker the deployment checklist") =>
        new("t", "s", content);

    // ---- the engines: a store whose own deadline fires must not sink a recall ------------------------

    [Fact]
    public async Task A_graph_recall_degrades_when_the_stores_own_deadline_fires()
    {
        var engine = new GraphMemoryEngine("graph", new TimingOutGraphStore(nameof(IMemoryGraphStore.SeedAsync)));

        var recall = await engine.RecallAsync(Query());

        Assert.Empty(recall.Items);
        Assert.Equal(MemorySources.None, recall.Ran);
    }

    [Fact]
    public async Task A_graph_recall_still_returns_its_hits_when_only_the_write_back_times_out()
    {
        // The per-PATH half: write-back runs AFTER the hits are found, so its timeout costs the learning
        // and never the answer. A whole-store double could not tell this from the fact above.
        var store = new TimingOutGraphStore(nameof(IMemoryGraphStore.WriteBackAsync),
            nameof(IMemoryGraphStore.TouchAsync), nameof(IMemoryGraphStore.LinkAsync));
        var engine = new GraphMemoryEngine("graph", store);
        await engine.RememberAsync(Write());

        var recall = await engine.RecallAsync(Query());

        Assert.NotEmpty(recall.Items);
    }

    /// <summary>Names one subject unconditionally, so the subject-index write is actually REACHED. Without an
    /// annotator the engine has no subjects to record, <c>RecordSubjectsAsync</c> is never called, and the
    /// test below passes against the unfixed engine — which is what the mutation probe caught.</summary>
    private sealed class AlwaysOneSubject : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(
            MemoryAnnotationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new MemoryAnnotation(["checklist"]));
    }

    [Fact]
    public async Task A_graph_write_lands_when_the_subject_index_times_out()
    {
        var store = new TimingOutGraphStore(nameof(IMemoryGraphStore.RecordSubjectsAsync));
        var engine = new GraphMemoryEngine("graph", store, annotation: new AlwaysOneSubject());

        await engine.RememberAsync(Write());

        Assert.NotEmpty((await engine.RecallAsync(Query())).Items);
    }

    [Fact]
    public async Task A_lexical_recall_degrades_when_a_BYO_stores_own_deadline_fires()
    {
        var engine = new LexicalMemoryEngine("lex", new TimingOutMemoryStore());

        var recall = await engine.RecallAsync(Query());

        Assert.Empty(recall.Items);
    }

    [Fact]
    public async Task A_curated_recall_degrades_when_a_BYO_stores_own_deadline_fires()
    {
        var engine = new CuratedMemoryEngine("curated", new TimingOutCuratedStore());

        var recall = await engine.RecallAsync(Query());

        Assert.Empty(recall.Items);
    }

    [Fact]
    public async Task A_semantic_recall_degrades_when_the_backends_own_deadline_fires()
    {
        var engine = new SemanticMemoryEngine("sem", new TimingOutSemanticMemory());

        var recall = await engine.RecallAsync(Query());

        Assert.Empty(recall.Items);
    }

    // ---- the seed sources ----------------------------------------------------------------------------

    [Fact]
    public async Task A_subject_seed_source_returns_empty_when_the_store_times_out()
    {
        var source = new SubjectSeedSource();
        var store = new TimingOutGraphStore(nameof(IMemoryGraphStore.KnownSubjectsAsync),
            nameof(IMemoryGraphStore.NodesBySubjectAsync));

        var seeds = await source.SeedAsync(new MemorySeedRequest("graph", store, Query(), 10), default);

        Assert.Empty(seeds);
    }

    // ---- the composing surfaces ----------------------------------------------------------------------

    [Fact]
    public async Task A_composite_keeps_the_healthy_members_material_when_one_member_times_out()
    {
        var composite = new CompositeMemoryEngine("blend",
            [new TimingOutEngine("slow"), new TimingOutEngine("fast", onRecall: false)]);

        var recall = await composite.RecallAsync(Query());

        Assert.NotEmpty(recall.Items);
    }

    [Fact]
    public async Task A_composition_yields_the_base_prompt_when_the_engine_times_out()
    {
        var composed = await new TimingOutEngine("slow").ComposeAsync("BASE", Query());

        Assert.Equal("BASE", composed);
    }

    [Fact]
    public async Task A_walk_yields_an_empty_first_step_when_the_first_recall_times_out()
    {
        var steps = new List<MemoryWalkStep>();
        await foreach (var step in new TimingOutEngine("slow").WalkAsync(Query())) steps.Add(step);

        var first = Assert.Single(steps);
        Assert.Empty(first.Items);
        Assert.Equal(MemorySources.None, first.Ran);
    }

    [Fact]
    public async Task A_walk_ends_with_the_last_good_step_when_an_expansion_times_out()
    {
        var engine = new TimingOutEngine("slow", onRecall: false, onExpand: true);

        var steps = new List<MemoryWalkStep>();
        await foreach (var step in engine.WalkAsync(Query())) steps.Add(step);

        // The first step is good and yielded; the faulting second is not yielded and the walk ends.
        var only = Assert.Single(steps);
        Assert.NotEmpty(only.Items);
    }

    // ---- the whole chain, which is the reason this is one suite --------------------------------------

    [Fact]
    public async Task A_timeout_deep_in_the_chain_degrades_at_every_layer_above_it()
    {
        // Store times out at the seed — the deepest link — and the caller is four layers up.
        var graph = new GraphMemoryEngine("graph", new TimingOutGraphStore(nameof(IMemoryGraphStore.SeedAsync)));
        var composite = new CompositeMemoryEngine("blend", [graph]);

        var composed = await composite.ComposeAsync("BASE", Query());
        var steps = new List<MemoryWalkStep>();
        await foreach (var step in composite.WalkAsync(Query())) steps.Add(step);

        Assert.Equal("BASE", composed);
        Assert.Empty(Assert.Single(steps).Items);
    }

    // ---- the controls: the caller's cancellation is still never swallowed ----------------------------

    [Theory]
    [InlineData("graph")]
    [InlineData("lexical")]
    [InlineData("curated")]
    [InlineData("semantic")]
    public async Task A_CALLER_cancelling_still_propagates_from_every_engine(string kind)
    {
        // The other half of every fact above. Without it the whole suite is satisfied by "swallow every
        // cancellation", which would make a cancelled recall look like a successful empty one.
        IMemoryEngine engine = kind switch
        {
            "graph" => new GraphMemoryEngine("graph", new InMemoryMemoryGraphStore()),
            "lexical" => new LexicalMemoryEngine("lex", new FakeMemoryStore()),
            "curated" => new CuratedMemoryEngine("curated", new FakeCuratedStore()),
            _ => new SemanticMemoryEngine("sem", new FakeSemanticMemory()),
        };
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.RecallAsync(Query(), cts.Token));
    }

    [Fact]
    public async Task A_CALLER_cancelling_still_propagates_from_a_walk()
    {
        var engine = new TimingOutEngine("slow", onRecall: false, onExpand: false);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in engine.WalkAsync(Query(), ct: cts.Token)) { }
        });
    }
}
