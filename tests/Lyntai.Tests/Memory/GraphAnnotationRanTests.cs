using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary><see cref="MemorySources.Annotation"/> on a graph write says the annotator ANSWERED and what it
/// answered was recorded — so a rebuild can retry a write that lost its subjects instead of guarding every
/// cause of losing them (<c>docs/DECISIONS.md</c> D175). On a recall it reports configuration, as
/// <see cref="MemorySources.Similarity"/> does.</summary>
public class GraphAnnotationRanTests
{
    private sealed class Answers(params string[] subjects) : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request, CancellationToken ct = default) =>
            Task.FromResult(new MemoryAnnotation(subjects));
    }

    private sealed class Throws : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("the model is down");
    }

    private static GraphMemoryEngine Engine(IMemoryAnnotationPolicy? annotator, IMemoryGraphStore? store = null) =>
        new("graph", store ?? new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams { Annotation = annotator });

    private static Task<MemoryWriteResult> Write(GraphMemoryEngine engine) =>
        engine.RememberAsync(new MemoryWrite("t", "s", "the deploy key is in the vault"));

    [Fact]
    public async Task An_annotator_that_answers_and_is_recorded_flags_the_write()
    {
        var result = await Write(Engine(new Answers("deploy key")));

        Assert.True(result.Ran.HasFlag(MemorySources.Annotation));
        Assert.True(result.Ran.HasFlag(MemorySources.Graph));
    }

    [Fact]
    public async Task An_annotator_that_answers_NO_subjects_still_answered()
    {
        var result = await Write(Engine(new Answers()));

        Assert.True(result.Ran.HasFlag(MemorySources.Annotation));
    }

    [Fact]
    public async Task A_failing_annotator_stores_the_fact_WITHOUT_the_flag()
    {
        var result = await Write(Engine(new Throws()));

        Assert.False(result.Ran.HasFlag(MemorySources.Annotation));
        Assert.NotEmpty(result.Reference.Id);                    // the fact itself is kept
    }

    [Fact]
    public async Task Subjects_the_store_refused_to_record_leave_the_flag_off()
    {
        // the model answered perfectly; the subject index refused the write — the subjects are lost all the same
        var result = await Write(Engine(new Answers("deploy key"), new SubjectHostileGraphStore()));

        Assert.False(result.Ran.HasFlag(MemorySources.Annotation));
    }

    [Fact]
    public async Task No_annotator_never_flags_a_write()
    {
        var result = await Write(Engine(annotator: null));

        Assert.False(result.Ran.HasFlag(MemorySources.Annotation));
    }

    [Fact]
    public async Task A_recall_reports_whether_an_annotator_is_wired()
    {
        var wired = Engine(new Answers("deploy key"));
        var bare = Engine(annotator: null);
        await Write(wired);
        await Write(bare);

        var withAnnotator = await wired.RecallAsync(new MemoryQuery("t", "s", "vault", 10));
        var without = await bare.RecallAsync(new MemoryQuery("t", "s", "vault", 10));

        Assert.True(withAnnotator.Ran.HasFlag(MemorySources.Annotation));
        Assert.False(without.Ran.HasFlag(MemorySources.Annotation));
    }
}
