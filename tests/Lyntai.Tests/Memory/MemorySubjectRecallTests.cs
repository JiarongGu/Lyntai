using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Seeding;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>Can a recall reach an entry through the SUBJECT it was indexed under?</b>
///
/// <para>Without a subject SEED, the handles <c>IMemoryAnnotationPolicy</c> produces at a model call per
/// write, and <c>RecordSubjectsAsync</c> stores, are read only at WRITE time — by the write path's own
/// linking and the annotator's reuse list. The handle 配偶 recorded against a fact whose text says 太太 is
/// then an index only its writer can use.</para>
///
/// <para>The pairs below are written so the target is <b>lexically unreachable</b>: a query sharing a word
/// with the content would prove nothing about subjects. Each headline fact therefore carries its own
/// control at <c>subjectSeedK: 0</c>, which is the same corpus and the same query with only the seed
/// switched off — if a control ever starts passing, the fact above it has stopped measuring the seed.</para>
/// </summary>
public class MemorySubjectRecallTests
{
    /// <summary><paramref name="subjectSeedK"/> is the handle channel's own knob — <c>0</c> is its documented
    /// off-switch, and the source stays REGISTERED at it, which is what keeps the controls below the same
    /// wiring as the facts they control for rather than a different one.</summary>
    private static GraphMemoryEngine NewEngine(TempDb db, IMemoryAnnotationPolicy? annotator,
        int subjectSeedK = 5) =>
        new("subjects", new SqliteMemoryGraphStore(db.Factory), seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Retrievability = new DsrRetrievability(),
                Ranking = new ReciprocalRankFusionPolicy(),
                Annotation = annotator,
                SeedSources = [new LexicalSeedSource(),
                    new SubjectSeedSource(new SubjectSeedOptions { K = subjectSeedK })],
            });

    private static IReadOnlyList<string> TextsOf(MemoryRecall recall) =>
        [.. recall.Items.Select(i => i.Content ?? i.Headline ?? string.Empty)];

    // The reported case, kept in its own words: the handle is 配偶 and the fact says 太太. The two share no
    // character, so nothing lexical can join them in either direction.
    private const string SpouseCn = "太太是麻醉师";
    private const string SpouseEn = "she works as an anaesthetist";

    private static Dictionary<string, string[]> OneFactAbout(string content, string subject) =>
        new(StringComparer.Ordinal) { [content] = [subject] };

    /// <summary><b>The headline, in the language it was reported in.</b> A recall for 配偶 reaches the fact
    /// recorded under that handle, whose own text never contains it.</summary>
    [Fact]
    public async Task A_recall_reaches_an_entry_through_the_subject_it_was_indexed_under()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator(OneFactAbout(SpouseCn, "配偶")));
        await engine.RememberAsync(new MemoryWrite("t", "s", SpouseCn));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "配偶", Limit: 10));

        Assert.Contains(SpouseCn, TextsOf(recall));
    }

    /// <summary>The control: the same corpus and the same query with the seed switched off returns nothing,
    /// so the fact above is measuring the seed rather than an accidental lexical hit.</summary>
    [Fact]
    public async Task With_the_subject_seed_off_the_same_query_reaches_nothing()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator(OneFactAbout(SpouseCn, "配偶")), subjectSeedK: 0);
        await engine.RememberAsync(new MemoryWrite("t", "s", SpouseCn));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "配偶", Limit: 10));

        Assert.Empty(recall.Items);
    }

    /// <summary>The same in a space-writing script, because the two take different branches of
    /// <see cref="MemorySubject.Matches"/> and a fact proved in one says nothing about the other.</summary>
    [Fact]
    public async Task A_spaced_subject_is_reachable_too()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator(OneFactAbout(SpouseEn, "spouse")));
        await engine.RememberAsync(new MemoryWrite("t", "s", SpouseEn));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "what does my spouse do", Limit: 10));

        Assert.Contains(SpouseEn, TextsOf(recall));
    }

    /// <summary>Its control, and it is not redundant with the CJK one: the boundary branch could match on a
    /// substring and this corpus would still pass the fact above.
    /// <para>The query is deliberately <c>"espouse a plan"</c> rather than <c>"espouse the idea"</c>:
    /// <c>"the"</c> is a substring of <c>"anaesthetist"</c>, so the LEXICAL seed would find the entry and the
    /// control could not distinguish a boundary bug from a working one.</para></summary>
    [Fact]
    public async Task A_query_that_only_contains_a_handle_inside_a_longer_word_reaches_nothing()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator(OneFactAbout(SpouseEn, "spouse")));
        await engine.RememberAsync(new MemoryWrite("t", "s", SpouseEn));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "espouse a plan", Limit: 10));

        Assert.Empty(recall.Items);
    }

    /// <summary><b>A subject match is a SEED, not an appended extra.</b> It enters the candidate set and is
    /// cut by the caller's limit like anything else — appending subject matches past the page would return
    /// more items than were asked for.</summary>
    [Fact]
    public async Task Subject_matches_stay_within_the_callers_limit()
    {
        using var db = new TempDb();
        var table = new Dictionary<string, string[]>(StringComparer.Ordinal);
        for (var i = 0; i < 6; i++) table[$"太太的第{i}件事"] = ["配偶"];
        var engine = NewEngine(db, new TableAnnotator(table));
        foreach (var content in table.Keys) await engine.RememberAsync(new MemoryWrite("t", "s", content));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "配偶", Limit: 2));

        Assert.Equal(2, recall.Items.Count);
    }

    /// <summary>A handle nobody asked about seeds nothing — the seed is driven by what the query NAMES, so
    /// an unrelated recall is unchanged by however many subjects the store holds.</summary>
    [Fact]
    public async Task A_subject_the_query_does_not_name_seeds_nothing()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator(OneFactAbout(SpouseCn, "配偶")));
        await engine.RememberAsync(new MemoryWrite("t", "s", SpouseCn));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "部署密钥在哪里", Limit: 10));

        Assert.Empty(recall.Items);
    }

    /// <summary>Scope and task still bound the seed. A subject index that reached across them would be a
    /// second, quieter way out of the isolation every other read path honours.</summary>
    [Fact]
    public async Task The_seed_does_not_cross_a_scope()
    {
        using var db = new TempDb();
        var engine = NewEngine(db, new TableAnnotator(OneFactAbout(SpouseCn, "配偶")));
        await engine.RememberAsync(new MemoryWrite("t", "s", SpouseCn));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "other", "配偶", Limit: 10));

        Assert.Empty(recall.Items);
    }

    /// <summary>An enumeration — a recall with no query — seeds no subjects. There is nothing to match
    /// against, and inventing "every subject matches" would make a no-query recall return the whole store in
    /// subject order.
    /// <para>Observed as subject-index CALLS, because the items cannot show it: a subject seed that fired would
    /// add the same entry the enumeration already returns. The queried recall afterwards is the control that
    /// the counters see a subject seed at all.</para></summary>
    [Fact]
    public async Task A_query_less_recall_seeds_no_subjects()
    {
        var store = new RecordingSubjectGraphStore();
        var engine = new GraphMemoryEngine("subjects", store, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Annotation = new TableAnnotator(OneFactAbout(SpouseCn, "配偶")),
                SeedSources = [new LexicalSeedSource(), new SubjectSeedSource(new SubjectSeedOptions { K = 5 })],
            });
        await engine.RememberAsync(new MemoryWrite("t", "s", SpouseCn));
        await engine.RememberAsync(new MemoryWrite("t", "s", "unrelated material"));
        var (known, bySubject) = (store.KnownSubjectsCalls, store.NodesBySubjectCalls);   // the writes' own reads

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", null, Limit: 10));

        Assert.Equal(2, recall.Items.Count);   // a null query enumerates
        Assert.Equal(known, store.KnownSubjectsCalls);
        Assert.Equal(bySubject, store.NodesBySubjectCalls);

        await engine.RecallAsync(new MemoryQuery("t", "s", "配偶", Limit: 10));
        Assert.True(store.NodesBySubjectCalls > bySubject, "a NAMED subject must reach the index, or the zero above proves nothing");
    }
}
