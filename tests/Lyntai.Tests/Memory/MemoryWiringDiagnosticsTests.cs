using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Seeding;
using Lyntai.Memory.Verification;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>A memory wiring that compiles, registers, resolves — and can never run. Both shapes were
/// reported by adopters on 3.0.0, and both are silent by construction: nothing throws, no result is missing,
/// and the only symptom is recall quality.</summary>
public class MemoryWiringDiagnosticsTests
{
    private static CompositeMemoryEngine Blend(MemoryWriteRouting routing, params IMemoryEngine[] members) =>
        new("project", members) { WriteRouting = routing };

    private static IMemoryEngine Member(string name, MemoryGrades grades) =>
        new RecordingEngine(name, grades);

    [Fact]
    public void A_member_whose_grades_an_earlier_member_already_claims_is_reported()
    {
        var blend = Blend(MemoryWriteRouting.FirstCapable,
            Member("project/graph", MemoryGrades.Associative | MemoryGrades.Authoritative),
            Member("project/semantic", MemoryGrades.Associative));

        var found = Assert.Single(MemoryWiring.Inspect([blend], verification: false, annotation: false));

        Assert.Contains("project/semantic", found, StringComparison.Ordinal);
        Assert.Contains("FanOutWrites", found, StringComparison.Ordinal);
    }

    /// <summary>The library's own documented blend must stay quiet. <c>UseCurated("glossary").UseGraph()</c>
    /// is in the README: curated takes the authoritative writes, the graph takes the associative ones, and
    /// neither is inert. A check that fired here would be wrong on correct wiring, which is worse than no
    /// check — it teaches a reader to ignore the channel.</summary>
    [Fact]
    public void The_documented_curated_then_graph_blend_reports_nothing()
    {
        var blend = Blend(MemoryWriteRouting.FirstCapable,
            Member("project/glossary", MemoryGrades.Authoritative),
            Member("project/graph", MemoryGrades.Associative | MemoryGrades.Authoritative));

        Assert.Empty(MemoryWiring.Inspect([blend], verification: false, annotation: false));
    }

    [Fact]
    public void Fanning_out_makes_the_shadowed_member_reachable_so_nothing_is_reported()
    {
        var blend = Blend(MemoryWriteRouting.EveryCapable,
            Member("project/graph", MemoryGrades.Associative | MemoryGrades.Authoritative),
            Member("project/semantic", MemoryGrades.Associative));

        Assert.Empty(MemoryWiring.Inspect([blend], verification: false, annotation: false));
    }

    /// <summary>Part 89C: <c>IMemoryVerificationPolicy</c> — "the single largest recall-quality lever the
    /// subsystem has" by its own registration doc — is consulted only by a graph member. Registered onto a
    /// blend with none, it never runs and the recall reports <c>Answered = null</c>, which is exactly what it
    /// reports with no judge registered at all.</summary>
    [Fact]
    public void A_verification_policy_no_engine_can_consult_is_reported()
    {
        var blend = Blend(MemoryWriteRouting.FirstCapable,
            new CuratedMemoryEngine("project/glossary", new FakeCuratedStore(), "glossary"));

        var found = Assert.Single(MemoryWiring.Inspect([blend], verification: true, annotation: false));

        Assert.Contains("IMemoryVerificationPolicy", found, StringComparison.Ordinal);
        Assert.Contains("UseGraph", found, StringComparison.Ordinal);
    }

    [Fact]
    public void An_annotation_policy_no_engine_can_consult_is_reported()
    {
        var blend = Blend(MemoryWriteRouting.FirstCapable,
            new SemanticMemoryEngine("project/semantic", new FakeSemanticMemory()));

        var found = Assert.Single(MemoryWiring.Inspect([blend], verification: false, annotation: true));

        Assert.Contains("IMemoryAnnotationPolicy", found, StringComparison.Ordinal);
    }

    /// <summary>Silence in the face of a BYO engine, deliberately. The two model-backed seams are consulted
    /// by <c>GraphMemoryEngine</c> alone TODAY, and a stranger's engine may well consult them — so the check
    /// only speaks when every member is one this library ships and knows does not.</summary>
    [Fact]
    public void An_unrecognised_engine_silences_the_policy_findings_rather_than_guessing()
    {
        var blend = Blend(MemoryWriteRouting.FirstCapable,
            new CuratedMemoryEngine("project/glossary", new FakeCuratedStore(), "glossary"),
            Member("project/mine", MemoryGrades.Associative));

        Assert.DoesNotContain(MemoryWiring.Inspect([blend], verification: true, annotation: true),
            f => f.Contains("IMemory", StringComparison.Ordinal));
    }

    /// <summary>Part 92: an embedder plus a vector store turns enrichment on, so every write is embedded for
    /// novelty and similarity linking — while the vector CHANNEL is opt-in, so no recall reads any of it. The
    /// consumer pays an embedding per write, gets vectors on disk, and sees no change in what recall returns;
    /// it cost an adopter most of a session to find, with every check green.</summary>
    [Fact]
    public void A_graph_member_that_embeds_every_write_and_seeds_no_recall_is_reported()
    {
        var graph = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            embedder: new FakeEmbedder(), vectors: new InMemoryVectorStore());

        var found = Assert.Single(MemoryWiring.Inspect([Blend(MemoryWriteRouting.FirstCapable, graph)],
            verification: false, annotation: false));

        Assert.Contains("AddMemorySemanticSeeds", found, StringComparison.Ordinal);
    }

    /// <summary>Registering the channel closes it — the finding is about the GAP between the two paths, not
    /// about having an embedder.</summary>
    [Fact]
    public void The_same_member_with_seeding_configured_reports_nothing()
    {
        var embedder = new FakeEmbedder();
        var vectors = new InMemoryVectorStore();
        var graph = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            embedder: embedder, vectors: vectors,
            seedSources: [new LexicalSeedSource(), new SemanticSeedSource(embedder, vectors)]);

        Assert.Empty(MemoryWiring.Inspect([Blend(MemoryWriteRouting.FirstCapable, graph)],
            verification: false, annotation: false));
    }

    /// <summary>…and so does having no embedder at all, which is the shipped default. A finding that fired
    /// on the zero-configuration path would fire on almost every consumer.</summary>
    [Fact]
    public void A_graph_member_with_no_embedder_reports_nothing()
    {
        var graph = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore());

        Assert.Empty(MemoryWiring.Inspect([Blend(MemoryWriteRouting.FirstCapable, graph)],
            verification: false, annotation: false));
    }

    /// <summary>Part 94, the same defect on the other index: an annotator pays a model call per write to
    /// record what a fact is ABOUT, and with the subject seed switched off nothing but the write path can
    /// read a handle. Reported by an adopter who had built the index and could not query it.</summary>
    [Fact]
    public void A_graph_member_that_records_subjects_and_seeds_no_recall_is_reported()
    {
        var graph = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            annotation: new FakeAnnotator(),
            seedSources: [new LexicalSeedSource()]);

        var found = Assert.Single(MemoryWiring.Inspect([Blend(MemoryWriteRouting.FirstCapable, graph)],
            verification: false, annotation: false));

        Assert.Contains("AddMemorySubjectSeeds", found, StringComparison.Ordinal);
    }

    /// <summary>The shipped default registers the handle channel, so the ordinary annotated wiring is clean —
    /// the finding is about the GAP between write and read, not about having an annotator.</summary>
    [Fact]
    public void An_annotated_member_on_the_default_options_reports_nothing()
    {
        var graph = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            annotation: new FakeAnnotator());

        Assert.Empty(MemoryWiring.Inspect([Blend(MemoryWriteRouting.FirstCapable, graph)],
            verification: false, annotation: false));
    }

    /// <summary>…and a member with no annotator never records a handle, so leaving the channel out is not a
    /// gap. Without this half the finding would fire on the zero-configuration path.</summary>
    [Fact]
    public void A_member_with_no_annotator_and_no_subject_seed_reports_nothing()
    {
        var graph = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            seedSources: [new LexicalSeedSource()]);

        Assert.Empty(MemoryWiring.Inspect([Blend(MemoryWriteRouting.FirstCapable, graph)],
            verification: false, annotation: false));
    }

    private sealed class FakeAnnotator : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request,
            CancellationToken ct = default) => Task.FromResult(MemoryAnnotation.None);
    }

    // ---- through a real container --------------------------------------------------------------------

    /// <summary>The end-to-end shape an adopter hit: <c>AddMemoryVerification()</c> on an engine with no
    /// graph member. Strict turns the warning into a startup failure, which is what makes it assertable
    /// without a log — the whole complaint was that the failure is silent.</summary>
    [Fact]
    public void Strict_wiring_turns_an_unconsultable_policy_into_a_startup_failure()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("chat", e => e.UseCurated("glossary").StrictWiring())
            .AddMemoryVerification());
        services.AddSingleton<ICuratedMemoryStore>(new FakeCuratedStore());
        using var sp = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IMemoryEngineFactory>());

        Assert.Contains("IMemoryVerificationPolicy", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>…and a graph member consults it, so the same registration is clean. Without this half the
    /// test above would pass over a check that fires on everything.</summary>
    [Fact]
    public void A_graph_member_consults_the_policy_so_the_same_registration_is_clean()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .UseInMemoryStorage()
            .AddMemoryEngine("chat", e => e.UseGraph().StrictWiring())
            .AddMemoryVerification());
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IMemoryEngineFactory>().Get("chat"));
    }

    /// <summary>Strict is per-engine to DECLARE and container-wide in EFFECT: a policy nothing consults is
    /// one fact about the container, so it is reported once rather than per engine — and one strict engine is
    /// enough to fail the build.</summary>
    [Fact]
    public void One_strict_engine_is_enough_to_fail_a_container_level_problem()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("lenient", e => e.UseCurated("glossary"))
            .AddMemoryEngine("strict", e => e.UseCurated("style").StrictWiring())
            .AddMemoryVerification());
        services.AddSingleton<ICuratedMemoryStore>(new FakeCuratedStore());
        using var sp = services.BuildServiceProvider();

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<IMemoryEngineFactory>());
    }

    /// <summary>The default is a warning, never a throw: an operator-curated catalog filled through its own
    /// store is a legitimate blend, and this library will not fail an application's startup over a
    /// configuration it cannot be sure is wrong.</summary>
    [Fact]
    public void Without_strict_wiring_the_same_container_resolves()
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("chat", e => e.UseCurated("glossary"))
            .AddMemoryVerification());
        services.AddSingleton<ICuratedMemoryStore>(new FakeCuratedStore());
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IMemoryEngineFactory>().Get("chat"));
    }

    /// <summary>Asking whether a policy is REGISTERED must not CONSTRUCT it: the shipped verification policy
    /// resolves an <c>ILlmClientFactory</c> with <c>GetRequiredService</c>, so a check that asked for the
    /// instance would turn a diagnostic into the startup failure it exists to describe. The doubles here
    /// throw from their factories, so constructing either fails this test rather than passing quietly.</summary>
    [Fact]
    public void The_registration_check_never_constructs_the_policy_it_asks_about()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryVerificationPolicy>(_ =>
            throw new InvalidOperationException("constructed, which the check must never do"));
        services.AddSingleton<IMemoryAnnotationPolicy>(_ =>
            throw new InvalidOperationException("constructed, which the check must never do"));
        services.AddSingleton<ICuratedMemoryStore>(new FakeCuratedStore());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("chat", e => e.UseCurated("glossary")));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IMemoryEngineFactory>().Get("chat"));
    }
}
