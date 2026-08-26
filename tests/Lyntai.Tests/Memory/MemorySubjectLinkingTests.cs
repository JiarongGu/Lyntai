using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>Does annotating a fact with what it is ABOUT actually connect an entity cluster?</b>
///
/// <para>The measured gap this answers: cluster recall sits at the no-graph floor
/// (<c>miss = 1 - 1/AttributeCount</c>) and is identical at recall limit 10 and 50, so those entries are
/// never gathered and no ranking policy can reach them. The edge census says why — 442 edges in English of
/// which 2 joined cluster members, 366 in Chinese of which 0 did.</para>
///
/// <para><b>A DETERMINISTIC annotator, deliberately.</b> The mechanism has to be proved before a prompt
/// exists, or a failure later is un-attributable: is the wiring wrong, or did the model answer badly? These
/// fakes answer perfectly by construction, so a failure here is the engine's.</para>
/// </summary>
public class MemorySubjectLinkingTests
{
    /// <summary>Annotates from a fixed content→subjects table. Stands in for a model that reads the write
    /// and its context; the point is that the SAME subject comes back for facts about one entity, which is
    /// the property the whole mechanism rests on.</summary>
    private sealed class TableAnnotator(Dictionary<string, string[]> subjectsByContent,
        MemoryGrade? grade = null) : IMemoryAnnotationPolicy
    {
        public int Calls { get; private set; }
        public List<int> ContextSizes { get; } = [];

        public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest request, CancellationToken ct = default)
        {
            Calls++;
            ContextSizes.Add(request.Recent.Count);
            return Task.FromResult(subjectsByContent.TryGetValue(request.Write.Content, out var subjects)
                ? new MemoryAnnotation(subjects, grade)
                : MemoryAnnotation.None);
        }
    }

    private sealed class ThrowingAnnotator : IMemoryAnnotationPolicy
    {
        public Task<MemoryAnnotation> AnnotateAsync(MemoryAnnotationRequest r, CancellationToken ct = default) =>
            throw new InvalidOperationException("the model is down");
    }

    // "my spouse is Alice" NAMES the subject; the other two do not — which is exactly the case that defeats
    // co-activation and every tokenizer, in any language.
    private const string Introduces = "my spouse is Alice";
    private const string Pronoun = "she works as an anaesthetist at a hospital";
    private const string Shared = "we met on a trip to Kyoto";

    private static Dictionary<string, string[]> SpouseCluster() => new(StringComparer.Ordinal)
    {
        [Introduces] = ["spouse"],
        [Pronoun] = ["spouse"],
        [Shared] = ["spouse"],
    };

    private static GraphMemoryEngine NewEngine(TempDb db, IMemoryAnnotationPolicy? annotator,
        GraphMemoryOptions? options = null) =>
        new("subjects", new SqliteMemoryGraphStore(db.Factory), options: options,
            agePolicies: [new PerWriteAgePolicy()],
            retrievability: new DsrRetrievability(), ranking: new ReciprocalRankFusionPolicy(),
            annotation: annotator);

    private static async Task WriteClusterAsync(GraphMemoryEngine engine)
    {
        foreach (var fact in new[] { Introduces, Pronoun, Shared })
            await engine.RememberAsync(new MemoryWrite("t", "s", fact));
    }

    /// <summary><b>The headline: a subject cue reaches facts that never contained the subject.</b> "she works
    /// at a hospital" and "we met in Kyoto" share no word with "my spouse is Alice" beyond pronouns, so they
    /// can arrive only across an edge — and before annotation there was no edge to cross.</summary>
    [Fact]
    public async Task An_annotated_cluster_is_reachable_from_a_subject_cue()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator(SpouseCluster()));
        await WriteClusterAsync(engine);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "my spouse", Limit: 10));
        var texts = recall.Items.Select(i => i.Content ?? i.Headline).ToList();

        Assert.Contains(texts, t => t!.Contains("anaesthetist", StringComparison.Ordinal));
        Assert.Contains(texts, t => t!.Contains("Kyoto", StringComparison.Ordinal));
    }

    /// <summary>The control that makes the fact above mean something: WITHOUT an annotator the same three
    /// writes and the same cue return only the entry that lexically matched. If this ever starts passing,
    /// the test above has stopped measuring the annotator.</summary>
    [Fact]
    public async Task Without_an_annotator_the_same_cue_reaches_only_the_lexical_match()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, annotator: null);
        await WriteClusterAsync(engine);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "my spouse", Limit: 10));
        var texts = recall.Items.Select(i => i.Content ?? i.Headline).ToList();

        // The POSITIVE half, and without it this control was vacuous: it asserted only two absences, so any
        // change making RecallAsync return nothing at all would have satisfied it — while the docstring above
        // promises "reaches only the lexical match". A control that passes when the mechanism it controls for
        // is dead is the failure it exists to rule out. Found 2026-08-14.
        Assert.NotEmpty(texts);
        Assert.Contains(texts, t => t!.Contains("Alice", StringComparison.Ordinal));

        Assert.DoesNotContain(texts, t => t!.Contains("anaesthetist", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, t => t!.Contains("Kyoto", StringComparison.Ordinal));
    }

    /// <summary>The annotator is shown recent entries — without them a pronoun is about nobody, and an
    /// implementation given only "she works at a hospital" cannot possibly return the right subject.</summary>
    [Fact]
    public async Task The_annotator_is_shown_recent_entries_as_context()
    {
        using var db = new TempDb();
        var annotator = new TableAnnotator(SpouseCluster());
        await WriteClusterAsync(NewEngine(db, annotator));

        Assert.Equal(3, annotator.Calls);
        Assert.Equal(0, annotator.ContextSizes[0]);      // nothing written yet
        Assert.True(annotator.ContextSizes[2] >= 2, "the third write should see the first two");
    }

    /// <summary><b>A failing annotator must not fail the write.</b> The model-free floor is the promise the
    /// whole subsystem rests on — memory that stops accepting facts because a model is down is worse than
    /// memory with no model at all.</summary>
    [Fact]
    public async Task A_failing_annotator_still_stores_the_fact()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new ThrowingAnnotator());

        await engine.RememberAsync(new MemoryWrite("t", "s", Introduces));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "spouse", Limit: 10));
        Assert.Contains(recall.Items, i => (i.Content ?? i.Headline)!.Contains("Alice", StringComparison.Ordinal));
    }

    /// <summary><b>A failing subject INDEX must not fail the write either</b> — the other link in the same
    /// chain, and the one the annotator fact above structurally cannot reach.
    /// <para>There the model never answers, so nothing arrives at the store. Here a perfect annotator
    /// answers and the projection refuses the write, which is the half that catch has never been asked
    /// about. Same invariant as the similarity index's
    /// (<c>GraphSimilarityTests.A_failing_vector_STORE_costs_links_not_the_entry</c>): a partial projection
    /// failure costs CONNECTIONS, never the fact.</para></summary>
    [Fact]
    public async Task A_failing_subject_index_costs_links_not_the_entry()
    {
        var engine = new GraphMemoryEngine("subjects", new SubjectHostileGraphStore(),
            agePolicies: [new PerWriteAgePolicy()], retrievability: new DsrRetrievability(),
            annotation: new TableAnnotator(SpouseCluster()));

        await engine.RememberAsync(new MemoryWrite("t", "s", Introduces));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "spouse", Limit: 10));
        Assert.Contains(recall.Items, i => (i.Content ?? i.Headline)!.Contains("Alice", StringComparison.Ordinal));
    }

    /// <summary>A suggested grade applies only when the caller did not state one — a model may advise what
    /// matters, never overrule what the application already decided.</summary>
    [Fact]
    public async Task A_suggested_grade_applies_only_when_the_write_did_not_state_one()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator(SpouseCluster(), MemoryGrade.Authoritative));

        await engine.RememberAsync(new MemoryWrite("t", "s", Introduces));                              // Inherit
        await engine.RememberAsync(new MemoryWrite("t", "s", Pronoun, Grade: MemoryGrade.Associative)); // explicit

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "spouse", Limit: 10));
        var suggested = recall.Items.Single(i => (i.Content ?? i.Headline)!.Contains("Alice", StringComparison.Ordinal));
        var explicitly = recall.Items.Single(i => (i.Content ?? i.Headline)!.Contains("anaesthetist", StringComparison.Ordinal));

        Assert.Equal(MemoryGrade.Authoritative, suggested.Grade);
        Assert.Equal(MemoryGrade.Associative, explicitly.Grade);
    }

    /// <summary>An annotator that returns no subjects links nothing, and an engine with one behaves exactly
    /// as an engine without — so registering an annotator that has no opinion costs correctness nothing.
    /// </summary>
    [Fact]
    public async Task An_annotator_with_no_opinion_changes_nothing()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator([]));
        await WriteClusterAsync(engine);

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "my spouse", Limit: 10));
        var texts = recall.Items.Select(i => i.Content ?? i.Headline).ToList();

        Assert.DoesNotContain(texts, t => t!.Contains("Kyoto", StringComparison.Ordinal));
    }

    /// <summary>
    /// <b>THE CASE THE STORED INDEX EXISTS FOR: a shared subject that NO entry names in its own text.</b>
    ///
    /// <para>The first version of this linked by SEARCHING for the subject, which needs some entry to be
    /// findable by it — normally the fact that introduces the entity ("my spouse is Alice" contains
    /// "spouse"). Three facts about one OWNER with different attributes have no such entry: "the spouse is
    /// Alice", "the deploy key is in the vault", "the client is northern logistics" are all about *me* and
    /// none contains "me". This test was written in the failing direction to prove that, and it did:
    /// searching linked nothing here.</para>
    ///
    /// <para><b>That mattered because it is exactly the corpus's attribute cluster</b> — and therefore
    /// exactly where the measured no-graph floor comes from. A search-based mechanism would have looked
    /// correct in every other test in this file and moved no measurement at all. <c>RecordSubjectsAsync</c>
    /// removes the dependency: the subject is stored, so it links whether or not any text mentions it.</para>
    /// </summary>
    [Fact]
    public async Task A_shared_subject_links_even_when_no_entry_names_it()
    {
        const string spouse = "the spouse is Alice";
        const string key = "the deploy key is in the vault";
        using var db = new TempDb();
        // every fact is about the same owner, and no fact contains the word "owner"
        var engine = NewEngine(db, new TableAnnotator(new(StringComparer.Ordinal)
        {
            [spouse] = ["owner"],
            [key] = ["owner"],
        }));

        await engine.RememberAsync(new MemoryWrite("t", "s", spouse));
        await engine.RememberAsync(new MemoryWrite("t", "s", key));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "spouse", Limit: 10));
        var texts = recall.Items.Select(i => i.Content ?? i.Headline).ToList();

        Assert.Contains(texts, t => t!.Contains("Alice", StringComparison.Ordinal));   // the lexical match
        Assert.Contains(texts, t => t!.Contains("vault", StringComparison.Ordinal));   // reached only by subject
    }

    /// <summary><b>The same mechanism in Chinese</b>, where it matters most: the tokenizer cannot connect
    /// these three either, and the annotator's judgement is language-independent — which is the argument for
    /// putting a model here rather than in the tokenizer.</summary>
    [Fact]
    public async Task An_annotated_cluster_is_reachable_from_a_subject_cue_in_chinese()
    {
        const string introduces = "我的配偶是爱丽丝";
        const string pronoun = "她在一家医院做麻醉师";
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator(new(StringComparer.Ordinal)
        {
            [introduces] = ["配偶"],
            [pronoun] = ["配偶"],
        }));

        await engine.RememberAsync(new MemoryWrite("t", "s", introduces));
        await engine.RememberAsync(new MemoryWrite("t", "s", pronoun));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "我的配偶", Limit: 10));
        var texts = recall.Items.Select(i => i.Content ?? i.Headline).ToList();

        Assert.Contains(texts, t => t!.Contains("麻醉师", StringComparison.Ordinal));
    }

    /// <summary><b>The reuse list is bounded, so in a long-lived memory a rarely-used handle DOES fall off —
    /// and this pins which one, because the eviction order is the whole design.</b>
    ///
    /// <para>`TASKS.md` Part 65 recorded this as unmeasured residue: <c>AnnotationKnownSubjects</c> caps how
    /// many existing handles an annotator is shown, and the list is ordered most-used-first, so a
    /// correct-but-singleton subject can drop out and be re-invented under a new name.</para>
    ///
    /// <para><b>Measured here, and the ordering is the right one.</b> The alternative — most-RECENT first —
    /// would evict a long-standing HUB handle the moment a burst of new subjects arrived, orphaning the
    /// largest cluster in the store. Most-used-first spends the bounded room on the handles the most facts
    /// already share, so the failure is confined to the smallest clusters: a singleton may fail to grow,
    /// while a cluster of many cannot be broken. That is the cheap direction for the failure to run in, and
    /// it is why this is a bound rather than a bug.</para>
    ///
    /// <para>Asserted from both ends — the hub must survive AND the singleton must be gone — because either
    /// alone would also pass on an implementation that simply returned everything or nothing.</para></summary>
    [Fact]
    public async Task The_reuse_list_evicts_the_least_used_handle_first_so_a_hub_cluster_cannot_be_broken()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = NewEngine(db, annotator: null);
        const int limit = 5;

        // Real nodes are required: RecordSubjectsAsync inserts by SELECTing from the node table (task_key
        // and scope are denormalized from the node), so a subject recorded against a nonexistent id is
        // silently dropped. The first draft of this test did exactly that and read back an empty list.
        async Task<long> WriteAsync(string content)
        {
            var reference = await engine.RememberAsync(new MemoryWrite("t", "s", content));
            return long.Parse(reference.Id, CultureInfo.InvariantCulture);
        }

        // one hub handle shared by many facts, plus more singletons than the list has room for
        for (var i = 0; i < 6; i++)
            await store.RecordSubjectsAsync("subjects", await WriteAsync($"hub fact number {i}"), ["hub"]);
        for (var i = 0; i < limit + 3; i++)
            await store.RecordSubjectsAsync("subjects", await WriteAsync($"lone fact number {i}"),
                [$"singleton{i}"]);

        var known = await store.KnownSubjectsAsync("subjects", "t", null, limit);

        Assert.Equal(limit, known.Count);
        Assert.Contains("hub", known);          // the many-fact handle survives the cap...
        var survivingSingletons = known.Count(k => k.StartsWith("singleton", StringComparison.Ordinal));
        Assert.Equal(limit - 1, survivingSingletons);   // ...and the rest of the singletons are evicted
    }
}
