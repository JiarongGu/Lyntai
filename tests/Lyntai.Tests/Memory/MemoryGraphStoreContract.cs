using Lyntai.Memory;

namespace Lyntai.Tests.Memory;

/// <summary>Backend-agnostic <see cref="IMemoryGraphStore"/> facts. MEM2b runs these against SQLite and
/// Postgres unchanged; per <c>storage.md</c> the contract IS the deduplication mechanism for the relational
/// pair, not a shared base class.
/// <para><b>Aging is done by WRITING</b>, not by advancing a clock: an entry's age is how far the engine's
/// position has moved since it was last used, and only writes move it. <see cref="Crowd"/> is how these
/// facts make something old.</para>
/// <para><b>MULTI-TOKEN matching is a PORTABLE guarantee as of 3.0, and was not before.</b> Until then only
/// SQLite's FTS path split a query into words; every other path matched the whole query as one contiguous
/// substring, so a realistic cue found the entry on one backend and nothing at all on another. That was
/// filed here as a by-design divergence, which it was not: an ORDERING difference is a divergence, a
/// different answer to "is the fact found" is a defect. <see cref="Lyntai.Storage.SearchTerms"/> is now the
/// one split, and <see cref="Seeding_matches_any_term_of_a_multi_word_query"/> holds every backend to it.
/// </para>
/// <para>What REMAINS deliberately omitted is same-match ORDERING — SQLite ranks by bm25, the others by
/// matched-term count then recency — exactly as <c>IMemoryStore.RecallAsync</c> documents.</para></summary>
public static class MemoryGraphStoreContract
{
    private const double Stability = 7;

    private static GraphNodeWrite Write(string engine, string key, string content,
        MemoryGrade grade = MemoryGrade.Associative, double advance = 1) =>
        new(engine, key, "s", content, content, grade, Stability, advance, null);

    /// <summary>Advance the engine's position by writing unrelated material in another scope — which is
    /// exactly what makes an entry stale: newer material competing with it.</summary>
    private static async Task Crowd(IMemoryGraphStore store, string engine, string key, int writes)
    {
        for (var i = 0; i < writes; i++)
            await store.UpsertAsync(new GraphNodeWrite(engine, key, "filler", $"filler {i}", $"filler {i}",
                MemoryGrade.Associative, Stability, 1, null));
    }

    public static async Task Upsert_then_seed_by_single_token_substring(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the deploy pipeline requires manual approval"));
        await store.UpsertAsync(Write("e", key, "rollbacks must page the on-call"));

        var hits = await store.SeedAsync("e", key, "s", "pipeline", 10);

        Assert.Single(hits);
        Assert.Contains("manual approval", hits[0].Content, StringComparison.Ordinal);
    }

    /// <summary><b>A multi-word cue matches an entry carrying ANY of its terms, on every backend.</b> This is
    /// the shape a real recall has — a model asking "what is the user's spouse called" rather than a bare
    /// keyword — and before 3.0 it worked only on SQLite's FTS path. Everywhere else the whole cue had to
    /// appear verbatim in the content, which it never does, so keyword seeding was effectively dead on
    /// Postgres and InMemory.</summary>
    public static async Task Seeding_matches_any_term_of_a_multi_word_query(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the user's spouse is Alice"));
        await store.UpsertAsync(Write("e", key, "rollbacks must page the on-call"));

        // no entry contains this phrase; the entry shares exactly one term with it
        var hits = await store.SeedAsync("e", key, "s", "what is the spouse called", 10);

        Assert.Contains(hits, h => h.Content == "the user's spouse is Alice");
    }

    /// <summary><b>The same guarantee in Chinese — a script that writes no spaces at all.</b> Whitespace
    /// splitting hands back a Chinese sentence as ONE token, so before 3.0 a Chinese cue could only ever be
    /// an exact-substring match; the language decided the recall semantics. Both backends index trigrams
    /// (SQLite <c>tokenize='trigram'</c>, Postgres <c>pg_trgm</c>), so
    /// <see cref="Lyntai.Storage.SearchTerms"/> expands a spaceless run into character trigrams and the two
    /// sentences below — neither a substring of the other — now match on the shared name.</summary>
    public static async Task Seeding_matches_a_chinese_query_without_spaces(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "用户的配偶是爱丽丝"));       // "the user's spouse is Alice"
        await store.UpsertAsync(Write("e", key, "回滚必须通知值班人员"));     // an unrelated note

        var hits = await store.SeedAsync("e", key, "s", "爱丽丝是谁", 10);   // "who is Alice"

        Assert.Contains(hits, h => h.Content == "用户的配偶是爱丽丝");
        Assert.DoesNotContain(hits, h => h.Content == "回滚必须通知值班人员");
    }

    /// <summary><b>A Chinese query whose ONLY overlap with the stored text is a TWO-character word still
    /// finds it.</b> Most Chinese content words are exactly two characters (配偶 spouse, 客户 client), so this
    /// is the common case rather than a corner.
    /// <para>A trigram index cannot match a two-character term — glue it to anything and the trigrams
    /// straddle the boundary and appear in no text containing the word. The two sentences below share only
    /// 配偶: every trigram of one is absent from the other, so the full-text path finds nothing, and the
    /// whole-query fallback fails too because neither sentence contains the other. Before 3.0 that was a
    /// miss.</para>
    /// <para><c>SearchTerms.LikeClause</c> now carries two-character terms for spaceless scripts on the
    /// SUBSTRING path, which has no index-imposed minimum. The fix is deliberately not in the FTS path,
    /// which structurally cannot use them.</para></summary>
    public static async Task Seeding_matches_a_two_character_chinese_word(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "我的配偶是爱丽丝"));       // "my spouse is Alice"
        await store.UpsertAsync(Write("e", key, "回滚必须通知值班人员"));   // an unrelated note

        var hits = await store.SeedAsync("e", key, "s", "配偶叫什么名字", 10);   // overlaps ONLY on 配偶

        Assert.Contains(hits, h => h.Content == "我的配偶是爱丽丝");
        Assert.DoesNotContain(hits, h => h.Content == "回滚必须通知值班人员");
    }

    /// <summary>Subjects round-trip, and the lookup is scoped to one task — a subject recorded in another
    /// task must not link across the boundary that keeps consumers isolated.</summary>
    public static async Task Subjects_round_trip_and_are_scoped_to_their_task(IMemoryGraphStore store, string key)
    {
        var mine = await store.UpsertAsync(Write("e", key, "a fact about the owner"));
        var theirs = await store.UpsertAsync(Write("e", key + "-other", "someone else's fact"));

        await store.RecordSubjectsAsync("e", mine, ["owner"]);
        await store.RecordSubjectsAsync("e", theirs, ["owner"]);

        Assert.Equal([mine], await store.NodesBySubjectAsync("e", key, "s", "owner", 10));
        Assert.Equal([theirs], await store.NodesBySubjectAsync("e", key + "-other", "s", "owner", 10));
        Assert.Empty(await store.NodesBySubjectAsync("e", key, "s", "nobody", 10));
    }

    /// <summary><b>Recording REPLACES rather than accumulates.</b> A stale subject from an earlier annotation
    /// would keep linking future facts into the wrong cluster, and nothing in the system would ever surface
    /// it — the failure would look like the memory simply being wrong about what relates to what.</summary>
    public static async Task Recording_subjects_replaces_the_previous_set(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "a fact whose annotation changes"));

        await store.RecordSubjectsAsync("e", id, ["first"]);
        await store.RecordSubjectsAsync("e", id, ["second"]);

        Assert.Empty(await store.NodesBySubjectAsync("e", key, "s", "first", 10));
        Assert.Equal([id], await store.NodesBySubjectAsync("e", key, "s", "second", 10));

        await store.RecordSubjectsAsync("e", id, []);   // an annotator that changes its mind to "no opinion"
        Assert.Empty(await store.NodesBySubjectAsync("e", key, "s", "second", 10));
    }

    /// <summary>A handle is matched through <see cref="MemorySubject"/>'s normalization — case-insensitively
    /// AND ignoring surrounding whitespace — so an annotator that varies either between writes still links
    /// them. The mechanism rests entirely on the same entity producing the same handle, and those two are
    /// the likeliest ways for a model to break that without meaning to.
    /// <para><b>Padding is asserted in both directions, and that is the half nothing used to cover.</b> Only
    /// case was pinned here, so deleting the <c>Trim()</c> from any backend's own copy of the rule left this
    /// fact green — the shape <c>pitfalls.md</c> §Testing names (mutate the behaviour and watch the test
    /// stay green). Recorded padded / looked up clean, and recorded clean / looked up padded, because a
    /// backend that trimmed on only ONE side would still satisfy either direction alone.</para>
    /// <para>De-duplication, the rule's third clause, is deliberately NOT asserted here: it is
    /// unobservable through this contract on every shipped backend (both SQL stores collapse a repeat on
    /// their unique index, and this lookup returns distinct node ids regardless), and a guard that cannot
    /// observe what it guards reads as coverage while providing none. <c>MemorySubjectTests</c> covers it
    /// where it IS observable — on the rule itself.</para></summary>
    public static async Task Subjects_match_case_insensitively(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "a fact"));

        await store.RecordSubjectsAsync("e", id, ["Spouse"]);

        Assert.Equal([id], await store.NodesBySubjectAsync("e", key, "s", "spouse", 10));
        Assert.Equal([id], await store.NodesBySubjectAsync("e", key, "s", "SPOUSE", 10));
        Assert.Equal([id], await store.NodesBySubjectAsync("e", key, "s", "  spouse\t", 10));

        var padded = await store.UpsertAsync(Write("e", key, "a second fact"));
        await store.RecordSubjectsAsync("e", padded, ["  Client  "]);

        Assert.Equal([padded], await store.NodesBySubjectAsync("e", key, "s", "client", 10));
    }

    /// <summary>A deleted node keeps no claim on its subjects — otherwise it would keep linking live facts
    /// to something that no longer exists, which is the subject-side twin of the dangling edge
    /// <see cref="Deleting_a_node_takes_its_edges_with_it"/> guards against.</summary>
    public static async Task Deleting_a_node_takes_its_subjects_with_it(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "a fact that will be deleted"));
        await store.RecordSubjectsAsync("e", id, ["owner"]);
        Assert.Equal([id], await store.NodesBySubjectAsync("e", key, "s", "owner", 10));

        await store.DeleteAsync("e", [id]);

        Assert.Empty(await store.NodesBySubjectAsync("e", key, "s", "owner", 10));

        // ...AND through the reader that does NOT join. Measured 2026-08-14: this fact asserted only through
        // NodesBySubjectAsync, whose JOIN against the node table hides an orphaned subject row — so it passed
        // on all three backends while neither SQL backend deleted the row at all (no foreign key, no cascade,
        // and DeleteAsync/PruneAsync/ForgetAsync touched only the node table). KnownSubjectsAsync does not
        // join, so it is the reader that can see the leak, and it is the one the annotator actually consumes:
        // GraphMemoryEngine feeds its top-N to the model as reuse candidates ordered by COUNT(*), so a fully
        // dead subject with many orphans outranks a live one and pushes real handles out of a bounded list.
        // The table also grew without bound, and ForgetAsync -- the user-facing erase -- left model-derived
        // subject strings in the database.
        Assert.DoesNotContain("owner", await store.KnownSubjectsAsync("e", key, "s", 50));
    }

    /// <summary><b>Tied subjects order — and therefore truncate — byte-ordinally on every backend.</b> The
    /// reuse list the annotator consumes is <c>COUNT(*)</c>-ordered with the subject itself as the
    /// tie-break, so WHICH handles survive a bounded list must not depend on the database's locale.
    /// <see cref="Lyntai.Storage.Postgres"/>'s sibling stores (vector, curated) already carry
    /// <c>COLLATE "C"</c> for exactly this, with comments saying it exists to match SQLite's BINARY order;
    /// this fact holds the one text ordering in the graph-store family to the same rule.
    /// <para>The pair below is chosen to catch a locale collation red-handed: byte order puts <c>zeta</c>
    /// (0x7A) before <c>état</c> (0xC3…), while any linguistic collation files é with e and reverses
    /// them.</para></summary>
    public static async Task Tied_subjects_order_and_truncate_byte_ordinally(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "a fact with two handles"));
        await store.RecordSubjectsAsync("e", id, ["zeta", "état"]);

        Assert.Equal(["zeta", "état"], await store.KnownSubjectsAsync("e", key, "s", 10));
        Assert.Equal(["zeta"], await store.KnownSubjectsAsync("e", key, "s", 1)); // truncation follows the order
    }

    public static async Task Upserting_identical_content_refreshes_rather_than_duplicating(
        IMemoryGraphStore store, string key)
    {
        var first = await store.UpsertAsync(Write("e", key, "one fact"));
        var second = await store.UpsertAsync(Write("e", key, "one fact"));

        Assert.Equal(first, second);
        Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
    }

    public static async Task Engines_are_isolated_from_one_another(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("engine-a", key, "belongs to a"));
        await store.UpsertAsync(Write("engine-b", key, "belongs to b"));

        var hits = await store.SeedAsync("engine-a", key, "s", null, 10);

        Assert.Single(hits);
        Assert.Equal("belongs to a", hits[0].Content);
    }

    /// <summary>A quiet engine must not be aged by a busy one — the reason the position is per engine
    /// rather than global.</summary>
    public static async Task A_busy_engine_does_not_age_a_quiet_ones_memories(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("quiet", key, "a fact nobody disturbs"));
        await Crowd(store, "busy", key, 200);

        var hits = await store.SeedAsync("quiet", key, "s", null, 10);

        Assert.Single(hits);
        Assert.Equal(0, hits[0].Age);
    }

    /// <summary>THE fact that pins the model: <b>decay buries, it does not cut.</b> A heavily crowded entry
    /// still comes back from a seed — being faint is not grounds for a store to withhold it. Hiding is the
    /// engine's job and it hides by RANK, so a faint memory alone in a quiet engine still surfaces and one
    /// under fresher material does not.
    /// <para>Also catches the silent failure the old faintness bound could hide: if a backend's age
    /// arithmetic yielded NULL, a predicate over it would have excluded every row while every other fact
    /// still passed.</para></summary>
    public static async Task Seeding_never_excludes_a_faint_entry(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "a note nobody has used in a long time"));
        await store.UpsertAsync(Write("e", key, "an exact fact", MemoryGrade.Authoritative));
        await Crowd(store, "e", key, 5000);

        var hits = await store.SeedAsync("e", key, "s", null, 10);

        Assert.Contains(hits, h => h.Content.Contains("long time", StringComparison.Ordinal));
        Assert.Contains(hits, h => h.Content.Contains("exact fact", StringComparison.Ordinal));
        // and it reports HOW faint, so the engine can rank it — the store judges nothing
        Assert.True(hits.First(h => h.Content.Contains("long time", StringComparison.Ordinal)).Age > 4000);
    }

    /// <summary><b>A word only in the HEADLINE is found, on every backend.</b> Found 2026-08-15:
    /// <c>lyntai_memory_node_fts</c> declares <c>headline, content</c> so SQLite matched one, while
    /// Postgres's trigram index and the in-process store read content alone — the same call answering
    /// differently per backend, which <c>storage.md</c> calls a defect rather than a difference.
    /// <para><b>Converged by WIDENING, and the first attempt got the direction wrong.</b> It confined
    /// SQLite's expression to <c>content</c>, reading this interface's portable guarantee as content-only.
    /// That guarantee states a MINIMUM ("is found on every backend"), not a ceiling — so SQLite was not
    /// exceeding a contract, and narrowing removed a real capability from the one backend that had it. An
    /// authored <c>MemoryWrite.Headline</c> is a summary a caller wrote so the entry could be found by it,
    /// with words that may appear nowhere in the content. Round 2 of that review caught it; Postgres gained
    /// a headline trigram index (<c>M202608152310_MemoryHeadlineSearch</c>) and the in-process store now
    /// reads both.</para></summary>
    public static async Task A_word_only_in_the_HEADLINE_matches_on_every_backend(
        IMemoryGraphStore store, string key)
    {
        // headline carries "kestrel"; content does not mention it at all
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "kestrel deployment notes",
            "the service listens on port 5001 behind a reverse proxy", MemoryGrade.Associative,
            Stability, 1, null));

        var seeded = await store.SeedAsync("e", key, "s", "kestrel", 10);

        Assert.Contains(seeded, n => n.Content.Contains("reverse proxy", StringComparison.Ordinal));
    }

    /// <summary>The control for the fact above: the same entry IS found by a word its content carries, so
    /// the assertion above is about the headline/content split rather than about the query failing to match
    /// anything at all. Without this, deleting the whole search path would satisfy the guarantee.</summary>
    public static async Task A_word_in_the_CONTENT_matches_on_every_backend(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "kestrel deployment notes",
            "the service listens on port 5001 behind a reverse proxy", MemoryGrade.Associative,
            Stability, 1, null));

        var seeded = await store.SeedAsync("e", key, "s", "proxy", 10);

        Assert.Contains(seeded, n => n.Content.Contains("reverse proxy", StringComparison.Ordinal));
    }

    /// <summary>The guarantee <see cref="IMemoryGraphStore.SeedAsync"/> states in prose: authoritative
    /// material is admitted whatever the query matched.
    /// <para>The QUERY path specifically — <see cref="Seeding_never_excludes_a_faint_entry"/> passes
    /// <c>query: null</c>, which takes the no-query branch on every backend and so cannot exercise the
    /// carve-out. The defect this guards: SQLite's FTS branch filtered on engine/task/scope only and
    /// returned early on any hit, so an exact fact sharing no trigram with the query was silently not
    /// seeded — ask about "restaurant" and the dietary constraint never reaches the prompt.</para></summary>
    public static async Task Seeding_admits_authoritative_material_the_query_does_not_match(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the user is vegetarian", MemoryGrade.Authoritative));
        await store.UpsertAsync(Write("e", key, "restaurant bookings need confirming"));

        // shares no trigram with the exact fact, so ONLY the associative note matches
        var hits = await store.SeedAsync("e", key, "s", "restaurant", 10);

        Assert.Contains(hits, h => h.Content == "the user is vegetarian");
        Assert.Contains(hits, h => h.Content == "restaurant bookings need confirming");
    }

    /// <summary>A grade-admitted node the query never matched reports <c>Relevance</c> exactly 0 — the same
    /// number on every backend (3.0). Before this, the three disagreed and SQLite disagreed with itself: its
    /// full-text path put such a row at the TAIL of the gradient, its substring-fallback path at the HEAD
    /// (grade leads that ORDER BY), Postgres at the head, and the in-process store reported a flat 1.
    /// <para><b>Two assertions, because only the pair is discriminating.</b> Asserting 0 alone would pass
    /// against a backend that reported 0 for EVERYTHING; the matching note must still carry a positive
    /// relevance, which is what makes this a fact about grade-admission rather than about zeroing.</para>
    /// <para>The query is one token of three or more characters, so SQLite takes its full-text path here and
    /// its substring fallback is covered by the short-query fact below — the two paths that used to answer
    /// this oppositely.</para></summary>
    public static async Task An_admitted_but_non_matching_exact_fact_reports_zero_relevance(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the user is vegetarian", MemoryGrade.Authoritative));
        await store.UpsertAsync(Write("e", key, "restaurant bookings need confirming"));

        var hits = await store.SeedAsync("e", key, "s", "restaurant", 10);

        var exact = Assert.Single(hits, h => h.Content == "the user is vegetarian");
        Assert.Equal(0, exact.Relevance, precision: 6);

        var matched = Assert.Single(hits, h => h.Content == "restaurant bookings need confirming");
        Assert.True(matched.Relevance > 0,
            $"the row the query DID match must keep a positive relevance; got {matched.Relevance}");
    }

    /// <summary>The same fact through the SHORT-query path — under three characters, which is below SQLite's
    /// trigram threshold and so takes its substring fallback, the path whose grade-first ordering used to
    /// report a non-matching exact fact at the HEAD of the gradient (relevance 1) rather than the tail.</summary>
    public static async Task An_admitted_but_non_matching_exact_fact_reports_zero_on_the_short_query_path(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the user is vegetarian", MemoryGrade.Authoritative));
        await store.UpsertAsync(Write("e", key, "ab testing notes"));

        var hits = await store.SeedAsync("e", key, "s", "ab", 10);

        var exact = Assert.Single(hits, h => h.Content == "the user is vegetarian");
        Assert.Equal(0, exact.Relevance, precision: 6);

        var matched = Assert.Single(hits, h => h.Content == "ab testing notes");
        Assert.True(matched.Relevance > 0,
            $"the row the query DID match must keep a positive relevance; got {matched.Relevance}");
    }

    /// <summary>Admission must survive the candidate LIMIT, not merely the WHERE clause.
    /// <para>An exact fact nobody has touched has the LOWEST <c>last_recalled_position</c> in its scope, so
    /// a plain recency ordering sorts it last and the LIMIT cuts it before the engine ranks anything. That
    /// is D41's own failure reached by a different route: the predicate no longer excludes a faint memory,
    /// but the ordering does.</para></summary>
    public static async Task Seeding_admits_a_long_quiet_exact_fact_over_fresher_material(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the user's spouse is Alice", MemoryGrade.Authoritative));

        // 60 fresher nodes IN THE SAME SCOPE — Crowd() writes to scope "filler" and so would not compete
        for (var i = 0; i < 60; i++)
            await store.UpsertAsync(Write("e", key, $"fresher note {i}"));

        var hits = await store.SeedAsync("e", key, "s", null, 10);

        Assert.Equal(10, hits.Count);
        Assert.Contains(hits, h => h.Content == "the user's spouse is Alice");
    }

    /// <summary>The BOUND on unconditional admission, stated so it cannot change silently: a scope holding
    /// more authoritative facts than the candidate limit still loses some. The guarantee is that the query
    /// never excludes them and that they outrank associative material — not that a finite budget holds them
    /// all.
    /// <para>Which ones survive is by recency and deliberately unspecified, so this asserts only the shape:
    /// the page fills with exact facts and the associative note is squeezed out.</para></summary>
    public static async Task Seeding_cannot_admit_more_exact_facts_than_the_limit(
        IMemoryGraphStore store, string key)
    {
        for (var i = 0; i < 8; i++)
            await store.UpsertAsync(Write("e", key, $"an exact fact numbered {i}", MemoryGrade.Authoritative));
        await store.UpsertAsync(Write("e", key, "the meeting notes need filing"));
        await store.UpsertAsync(Write("e", key, "the meeting ran long"));

        // a query only the associative notes match, so SQLite takes its FTS branch and the exact facts
        // arrive through the separate exact-facts query that carries this bound
        var hits = await store.SeedAsync("e", key, "s", "meeting", 5);

        Assert.Equal(5, hits.Count);
        Assert.All(hits, h => Assert.Equal(MemoryGrade.Authoritative, h.Grade));
    }

    /// <summary>Signals survive a round trip, including the values a careless serializer mangles. A lost
    /// signal has no symptom: the entry simply decays as if it had never been judged.</summary>
    public static async Task Signals_round_trip_through_the_store(IMemoryGraphStore store, string key)
    {
        var signals = MemorySignals.Empty
            .With(MemorySignals.WellKnown.Salience, 2.5)
            .With("negative", -1.25)
            .With("very.small", 0.000001)
            .With("unicode-名前", 3);

        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "signals ride along",
            MemoryGrade.Associative, 7, 1, null, signals));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));

        Assert.Equal(4, node.Signals.Count);
        Assert.Equal(2.5, node.Signals.Get(MemorySignals.WellKnown.Salience), 9);
        Assert.Equal(-1.25, node.Signals.Get("negative"), 9);
        Assert.Equal(0.000001, node.Signals.Get("very.small"), 9);
        Assert.Equal(3, node.Signals.Get("unicode-名前"), 9);
    }

    /// <summary>An entry written before signals existed reads back as an empty bag, not as null and not as
    /// a throw — every row in an upgraded database is one of these.</summary>
    public static async Task A_node_with_no_signals_reads_back_empty(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "no signals here",
            MemoryGrade.Associative, 7, 1, null));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));

        Assert.Equal(0, node.Signals.Count);
        Assert.Equal(1, node.Signals.Get(MemorySignals.WellKnown.Salience, fallback: 1));
    }

    /// <summary>THE fact that pins salience's new job: it is now decay resistance AND admission priority
    /// (2026-08-09 reversal — see the Task 5 brief's amendment). A salient ASSOCIATIVE entry — no grade
    /// carve-out to lean on — nobody has touched in a long while must still survive the candidate LIMIT
    /// against a wall of fresher, unjudged material, exactly the way an authoritative fact survives it by
    /// grade in <see cref="Seeding_admits_a_long_quiet_exact_fact_over_fresher_material"/>.
    /// <para><b>Mutation target.</b> Deleting the <c>salience DESC</c> ordering term collapses this entry back
    /// to a plain recency order, where it holds the single lowest <c>last_recalled_position</c> in the scope
    /// and the limit cuts it before anything is ranked — this fact would then fail.</para></summary>
    public static async Task Seeding_admits_a_long_quiet_salient_entry_over_fresher_material(
        IMemoryGraphStore store, string key)
    {
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 5);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "a salient note", "a salient note nobody has used in a long time",
            MemoryGrade.Associative, Stability, 1, null, signals));

        // 60 fresher, unjudged (default salience 1) nodes IN THE SAME SCOPE
        for (var i = 0; i < 60; i++)
            await store.UpsertAsync(Write("e", key, $"fresher note {i}"));

        var hits = await store.SeedAsync("e", key, "s", null, 10);

        Assert.Equal(10, hits.Count);
        Assert.Contains(hits, h => h.Content == "a salient note nobody has used in a long time");
    }

    /// <summary>A NON-FINITE salience is the neutral value everywhere — the shared leg of a fact that used to
    /// be enforced on ONE store of three, which is the exact shape of defect this branch already hit once.
    /// <para>Reachable through the public <see cref="IMemorySaliencePolicy"/> seam, and it failed differently on
    /// every backend: SQLite refused to bind <c>NaN</c> and the whole WRITE threw;
    /// <see cref="Lyntai.Storage.InMemory.InMemoryMemoryGraphStore"/> ordered the raw bag value, where
    /// <c>Comparer&lt;double&gt;</c> ranks <c>NaN</c> under every real number, so the entry sorted DEAD LAST
    /// and the limit cut it; Postgres bound it happily into the promoted column, where SQL sorts <c>NaN</c>
    /// ABOVE every real number, so it silently outranked a genuinely salient entry. The two assertions below
    /// catch all three.</para>
    /// <para>Deliberately behavioural, not a column read: the two SQL backends additionally pin the coerced
    /// COLUMN content in their own test classes, which no portable fact can reach — and this store's own bag
    /// keeps the raw value, since the coercion is a READ rule
    /// (<see cref="MemorySignals.Salience"/>), not a rewrite of what was stored.</para></summary>
    public static async Task Seeding_treats_a_non_finite_salience_as_the_neutral_value(
        IMemoryGraphStore store, string key)
    {
        var salient = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 5);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a genuinely salient note",
            MemoryGrade.Associative, Stability, 1, null, salient));

        for (var i = 0; i < 12; i++) await store.UpsertAsync(Write("e", key, $"fresher note {i}"));

        // written LAST, so under the neutral value it is the FRESHEST unjudged-equivalent row and lands
        // second — which is what makes "sorted away" and "sorted to the front" both visible below
        var broken = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, double.NaN);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a non-finite judgement",
            MemoryGrade.Associative, Stability, 1, null, broken));

        var hits = await store.SeedAsync("e", key, "s", null, 5);

        var broke = hits.Select(h => h.Content).ToList();
        // not sorted below every unjudged row (the in-process failure), and not sorted above a genuinely
        // salient one (the Postgres failure)
        Assert.Contains("a non-finite judgement", broke, StringComparer.Ordinal);
        Assert.True(broke.IndexOf("a genuinely salient note") < broke.IndexOf("a non-finite judgement"),
            $"a NaN salience outranked a genuine one: {string.Join(" | ", broke)}");
    }

    /// <summary>A salience BELOW the neutral 1 is the neutral value too. Nothing in the model means "less
    /// findable than an entry nobody ever judged", so a half-judged entry must order LEVEL with an
    /// unjudged one — never beneath it.
    /// <para>The SQL backends coerced this into their promoted column from the start while the in-process
    /// store ordered the raw bag, so <c>{salience: 0.5}</c> admitted differently on the same data, same
    /// query, different backend. <see cref="MemorySignals.Salience"/> is now the one rule all three read
    /// through.</para></summary>
    public static async Task Seeding_treats_a_below_neutral_salience_as_the_neutral_value(
        IMemoryGraphStore store, string key)
    {
        for (var i = 0; i < 12; i++) await store.UpsertAsync(Write("e", key, $"fresher note {i}"));

        var timid = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 0.5);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a half-judged note",
            MemoryGrade.Associative, Stability, 1, null, timid));

        var hits = await store.SeedAsync("e", key, "s", null, 5);

        // the freshest row in the scope, and neutral — so it leads, exactly as an unjudged row would
        Assert.Equal("a half-judged note", hits[0].Content);
    }

    /// <summary>The other half of the store-side reinforcement guarantee InMemoryMemoryGraphStore has had
    /// since Task 4 (see its UpsertAsync comment): a salience policy that DECLINES to judge a re-remembered write
    /// — too few comparables, a novelty probe that found only the entry's own prior vector, a caught failure
    /// — reports <see cref="MemorySignals.Empty"/>, and that must never be read as "this entry is no longer
    /// salient". A blanket overwrite would let the very write that is supposed to REINFORCE an entry instead
    /// erase an earlier judgement.</summary>
    public static async Task Re_remembering_with_no_signals_keeps_the_existing_bag(
        IMemoryGraphStore store, string key)
    {
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 5);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "reinforced fact",
            MemoryGrade.Associative, Stability, 1, null, signals));

        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "reinforced fact",
            MemoryGrade.Associative, Stability, 1, null, MemorySignals.Empty));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(5, node.Signals.Get(MemorySignals.WellKnown.Salience), 9);
    }

    /// <summary>The mutation guard against a blanket "keep whatever is already stored" implementation of
    /// the fact above: a fresh judgement that DOES report something must win, or a stale judgement could never
    /// be corrected. A store that always preserves the old value passes
    /// <see cref="Re_remembering_with_no_signals_keeps_the_existing_bag"/> but fails this one.</summary>
    public static async Task Re_remembering_with_fresh_signals_replaces_the_existing_bag(
        IMemoryGraphStore store, string key)
    {
        var first = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 5);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "rejudged fact",
            MemoryGrade.Associative, Stability, 1, null, first));

        var second = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 2);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "rejudged fact",
            MemoryGrade.Associative, Stability, 1, null, second));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(2, node.Signals.Get(MemorySignals.WellKnown.Salience), 9);
    }

    /// <summary>Both provenance columns round-trip (design doc §5.7, Task 4) — read and written only
    /// through <see cref="GraphNode.ProvenanceRetrievability"/>/<see cref="GraphNode.ProvenanceSalience"/>
    /// and <see cref="GraphNodeWrite.ProvenanceRetrievability"/>/<see cref="GraphNodeWrite.ProvenanceSalience"/>,
    /// exactly like <see cref="Signals_round_trip_through_the_store"/> pins for the signals bag
    /// itself.</summary>
    public static async Task Provenance_round_trips_through_the_store(IMemoryGraphStore store, string key)
    {
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 2);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "provenance rides along",
            MemoryGrade.Associative, Stability, 1, null, signals,
            ProvenanceRetrievability: 0x2, ProvenanceSalience: 0x1));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));

        Assert.Equal(0x2, node.ProvenanceRetrievability);
        Assert.Equal(0x1, node.ProvenanceSalience);
    }

    /// <summary>An entry written before this domain existed — every row through the ordinary
    /// <see cref="Write"/> helper, which never sets either provenance column — reads back as
    /// <c>None</c> (0) on both, never null and never a throw, mirroring
    /// <see cref="A_node_with_no_signals_reads_back_empty"/> for the signals bag itself.</summary>
    public static async Task A_node_with_no_provenance_reads_back_as_none(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "no provenance here"));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));

        Assert.Equal(0, node.ProvenanceRetrievability);
        Assert.Equal(0, node.ProvenanceSalience);
    }

    /// <summary>A plain re-remember of identical content refreshes the node but must not pick up whatever
    /// provenance the second write happens to carry — the same "never revisited" rule
    /// <see cref="GraphNode.Stability"/> itself already gets on a refresh (neither is in a store's
    /// <c>DO UPDATE SET</c>/refresh branch). Only <see cref="IMemoryGraphStore.TouchAsync"/> may change
    /// it.</summary>
    public static async Task A_plain_re_remember_does_not_touch_retrievability_provenance(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "reinforced fact",
            MemoryGrade.Associative, Stability, 1, null, ProvenanceRetrievability: 0x1));

        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "reinforced fact",
            MemoryGrade.Associative, Stability, 1, null, ProvenanceRetrievability: 0x2));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(0x1, node.ProvenanceRetrievability);
    }

    /// <summary>The provenance counterpart to <see cref="Re_remembering_with_no_signals_keeps_the_existing_bag"/>:
    /// a salience policy that declines this time (an empty incoming bag) must not blank the earlier attribution
    /// any more than it may blank the bag itself.</summary>
    public static async Task Re_remembering_with_no_signals_keeps_the_existing_salience_provenance(
        IMemoryGraphStore store, string key)
    {
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 5);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "reinforced fact",
            MemoryGrade.Associative, Stability, 1, null, signals, ProvenanceSalience: 0x1));

        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "reinforced fact",
            MemoryGrade.Associative, Stability, 1, null, MemorySignals.Empty, ProvenanceSalience: 0x2));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(0x1, node.ProvenanceSalience);
    }

    /// <summary>The mutation guard against a blanket "keep whatever is already stored", mirroring
    /// <see cref="Re_remembering_with_fresh_signals_replaces_the_existing_bag"/>: a fresh judgement that
    /// DOES report something must replace the earlier attribution, or a stale one could never be
    /// corrected.</summary>
    public static async Task Re_remembering_with_fresh_signals_replaces_the_salience_provenance(
        IMemoryGraphStore store, string key)
    {
        var first = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 5);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "rejudged fact",
            MemoryGrade.Associative, Stability, 1, null, first, ProvenanceSalience: 0x1));

        var second = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 2);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "rejudged fact",
            MemoryGrade.Associative, Stability, 1, null, second, ProvenanceSalience: 0x2));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(0x2, node.ProvenanceSalience);
    }

    /// <summary>The one moment retrievability provenance DOES change after the first write: a touch
    /// overwrites it with whatever policy computed THIS reinforcement — the exact parallel
    /// <see cref="Touch_records_reinforcement"/> already pins for <see cref="GraphNode.Stability"/>
    /// itself.</summary>
    public static async Task Touch_updates_the_retrievability_provenance(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "reinforced",
            MemoryGrade.Associative, Stability, 1, null, ProvenanceRetrievability: 0x1));
        await Crowd(store, "e", key, 3);

        await store.TouchAsync("e", [new GraphTouch(id, 10.5, 0x2)]);

        var node = await store.GetAsync("e", id);
        Assert.NotNull(node);
        Assert.Equal(0x2, node!.ProvenanceRetrievability);
    }

    // ---- difficulty (2026-08-10, fsrs-properly plan Task 2): a SECOND writer besides the salience policy now
    // exists (the retrievability policy, via TouchAsync), which is what makes this different from salience
    // — see MemoryDecayState.Difficulty's own remarks for the full precedence rule between the two. ----

    /// <summary>Seeded from the incoming signal at first write, coerced the one documented way
    /// (<see cref="MemorySignals.Difficulty"/>) — the same round trip <see cref="Signals_round_trip_through_the_store"/>
    /// pins for the bag itself, now for the promoted column.</summary>
    public static async Task Difficulty_is_seeded_from_the_signal_at_first_write(IMemoryGraphStore store, string key)
    {
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, 7);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a judged-difficult note",
            MemoryGrade.Associative, Stability, 1, null, signals));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(7, node.Difficulty, 9);
    }

    /// <summary>An entry written before this field existed — or before it was ever judged — reads back at
    /// the neutral value (the mid-point <c>5</c>, corrected 2026-08-11 from the floor <c>1</c> — see
    /// <see cref="Lyntai.Memory.Forgetting.DsrOptions.NeutralDifficulty"/>'s own remarks), never null, never
    /// a throw, mirroring <see cref="A_node_with_no_signals_reads_back_empty"/> for the bag itself.</summary>
    public static async Task A_node_with_no_difficulty_signal_reads_back_neutral(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "no difficulty judged here"));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(5, node.Difficulty, 9);
    }

    /// <summary>The precedence rule's first half: a salience policy that DECLINES to judge a re-remembered write
    /// (an empty incoming bag) must not blank whatever the retrievability policy has since tracked — the
    /// exact parallel <see cref="Re_remembering_with_no_signals_keeps_the_existing_bag"/> pins for the bag,
    /// and <see cref="Re_remembering_with_no_signals_keeps_the_existing_salience_provenance"/> pins for
    /// provenance.</summary>
    public static async Task Re_remembering_with_no_signals_keeps_the_existing_difficulty(
        IMemoryGraphStore store, string key)
    {
        var signals = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, 8);
        var id = await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a difficulty-tracked fact",
            MemoryGrade.Associative, Stability, 1, null, signals));
        // simulate what a retrievability policy would have tracked since the first write — a plain
        // re-remember with an EMPTY bag must not reset this back toward the write-time judgement either
        await store.TouchAsync("e", [new GraphTouch(id, Stability, 0, 3.5)]);

        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a difficulty-tracked fact",
            MemoryGrade.Associative, Stability, 1, null, MemorySignals.Empty));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(3.5, node.Difficulty, 9);
    }

    /// <summary>THE fix-round-1 C1 fact — the bug the two facts either side of this one could not catch,
    /// because both only ever exercise an EMPTY incoming bag. Difficulty has a SECOND writer salience does
    /// not (the retrievability policy, via <see cref="IMemoryGraphStore.TouchAsync"/>), so its precedence
    /// must key on whether THIS write's bag actually NAMES a difficulty signal — not on whether the bag is
    /// merely non-empty, which is <c>salience</c>'s own rule. A write that judges something else entirely
    /// (salience alone, here) must leave whatever <c>Reinforce</c> has since tracked exactly as it was.
    /// <para><b>Mutation target.</b> An implementation keyed on "the bag is non-empty" (matching
    /// <c>salience</c>'s own promoted-column rule exactly, which is what a first draft of this shipped) fails
    /// this fact: the salience-only re-remember below would silently reset difficulty toward the write-time
    /// judgement (8) or the neutral default, not leave the tracked value (3.5) alone.</para></summary>
    public static async Task Re_remembering_with_an_unrelated_signal_does_not_touch_the_tracked_difficulty(
        IMemoryGraphStore store, string key)
    {
        var difficultySignal = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, 8);
        var id = await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a difficulty-tracked fact",
            MemoryGrade.Associative, Stability, 1, null, difficultySignal));
        // simulate what a retrievability policy would have tracked since the first write
        await store.TouchAsync("e", [new GraphTouch(id, Stability, 0, 3.5)]);

        // a NON-EMPTY bag that names something else ENTIRELY — no difficulty key at all — must not touch
        // the tracked value, unlike a judgement that DOES name difficulty (the fact above this one)
        var salienceOnly = MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, 2);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a difficulty-tracked fact",
            MemoryGrade.Associative, Stability, 1, null, salienceOnly));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(3.5, node.Difficulty, 9);
    }

    /// <summary>The precedence rule's second half, and the mutation guard against a blanket "keep whatever
    /// is already stored" implementation of the fact above: an explicit fresh judgement on ANY write — fresh
    /// or a re-remember — must win over whatever the retrievability policy has tracked since, or a
    /// consumer's explicit correction could never take effect. Mirrors
    /// <see cref="Re_remembering_with_fresh_signals_replaces_the_existing_bag"/>.</summary>
    public static async Task Re_remembering_with_fresh_signals_replaces_the_difficulty(
        IMemoryGraphStore store, string key)
    {
        var first = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, 8);
        var id = await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a rejudged fact",
            MemoryGrade.Associative, Stability, 1, null, first));
        // the retrievability policy tracked a DIFFERENT value since the first write
        await store.TouchAsync("e", [new GraphTouch(id, Stability, 0, 2)]);

        var second = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, 4);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a rejudged fact",
            MemoryGrade.Associative, Stability, 1, null, second));

        var node = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal(4, node.Difficulty, 9);
    }

    /// <summary>The one moment difficulty changes without an explicit fresh judgement: a touch overwrites it
    /// with whatever the retrievability policy computed THIS reinforcement — the exact parallel
    /// <see cref="Touch_records_reinforcement"/> pins for <see cref="GraphNode.Stability"/> and
    /// <see cref="Touch_updates_the_retrievability_provenance"/> pins for provenance.</summary>
    public static async Task Touch_updates_difficulty(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "reinforced"));
        await Crowd(store, "e", key, 3);

        await store.TouchAsync("e", [new GraphTouch(id, 10.5, 0, 6.25)]);

        var node = await store.GetAsync("e", id);
        Assert.NotNull(node);
        Assert.Equal(6.25, node!.Difficulty, 9);
    }

    /// <summary>Coercion is pinned as BEHAVIOUR here (the exact rule is
    /// <see cref="MemorySignals.Difficulty"/>, and every promotion calls it) — a non-finite or
    /// out-of-range signal must never reach the store as anything other than its clamped/neutral
    /// value, mirroring <see cref="Seeding_treats_a_non_finite_salience_as_the_neutral_value"/> and
    /// <see cref="Seeding_treats_a_below_neutral_salience_as_the_neutral_value"/> for salience.</summary>
    public static async Task Seeding_treats_a_non_finite_or_out_of_range_difficulty_signal_as_coerced(
        IMemoryGraphStore store, string key)
    {
        var high = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, 1e9);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "an absurdly hard note",
            MemoryGrade.Associative, Stability, 1, null, high));
        var low = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, -5);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a negative-difficulty note",
            MemoryGrade.Associative, Stability, 1, null, low));
        var broken = MemorySignals.Empty.With(MemorySignals.WellKnown.Difficulty, double.NaN);
        await store.UpsertAsync(new GraphNodeWrite("e", key, "s", "h", "a non-finite judgement",
            MemoryGrade.Associative, Stability, 1, null, broken));

        var hits = await store.SeedAsync("e", key, "s", null, 10);

        // High/low are EXPLICIT out-of-range judgements — clamped to the scale's own bounds, unchanged by
        // the 2026-08-11 neutral correction (that correction only touches what "no information at all"
        // means, never the clamp bounds themselves).
        Assert.Equal(10, hits.Single(h => h.Content == "an absurdly hard note").Difficulty, 9);
        Assert.Equal(1, hits.Single(h => h.Content == "a negative-difficulty note").Difficulty, 9);
        // Non-finite is NOT an explicit judgement — it means no real information was supplied, so it reads
        // as the NEUTRAL value (5, corrected 2026-08-11 from the floor 1), not the floor.
        Assert.Equal(5, hits.Single(h => h.Content == "a non-finite judgement").Difficulty, 9);
    }

    public static async Task A_bigger_write_ages_more(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the entry being crowded"));
        // one write, but the age policy said it was worth 40 — the ContentSizeAgePolicy shape
        await store.UpsertAsync(Write("e", key, "one very large document", advance: 40));

        var hits = await store.SeedAsync("e", key, "s", "crowded", 10);

        Assert.Equal(40, hits.Single().Age, precision: 6);
    }

    /// <summary>The write-count primitive (design doc §5.7): <see cref="GraphNode.OrdinalAge"/> counts writes
    /// since this entry was last used, UNCONDITIONALLY — never scaled by whatever
    /// <see cref="GraphNodeWrite.Advance"/> a caller's currently-installed
    /// <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/> happened to compute (contrast
    /// <see cref="A_bigger_write_ages_more"/>, which pins that <see cref="GraphNode.Age"/> — the OLDER,
    /// coexisting mechanism this task does not touch — still reads that scaled value).</summary>
    public static async Task Ordinal_age_counts_writes_since_last_use(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the entry being crowded"));
        // a big Advance must NOT inflate OrdinalAge — it is one write, regardless of what the caller's own
        // policy said this write was worth
        await store.UpsertAsync(Write("e", key, "one very large document", advance: 40));

        var hits = await store.SeedAsync("e", key, "s", "crowded", 10);

        Assert.Equal(1, hits.Single().OrdinalAge, precision: 6);
    }

    /// <summary>The content-volume primitive (design doc §5.7): <see cref="GraphNode.VolumeAge"/> counts
    /// characters written to the engine since this entry was last used, unconditionally — what
    /// <see cref="Lyntai.Memory.Interference.ContentSizeAgePolicy"/> projects its own age from.</summary>
    public static async Task Volume_age_counts_characters_written_since_last_use(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "the entry being crowded"));
        const string document = "a document of a known, exact length";
        await store.UpsertAsync(Write("e", key, document));

        var hits = await store.SeedAsync("e", key, "s", "crowded", 10);

        Assert.Equal(document.Length, hits.Single().VolumeAge, precision: 6);
    }

    /// <summary>The real-time primitive (design doc §5.7): <see cref="GraphNode.ElapsedAge"/> is a REAL,
    /// controllable quantity — not merely "0 unless untouched" (fix round 1, C2). Requires the store to be
    /// built over a controllable clock (see <paramref name="advance"/>), matching
    /// <c>MemoryStoreContract</c>'s own TTL facts.
    /// <para><b>Mutation target.</b> A store that returns a constant (or mishandles a timestamp's offset on
    /// round-trip) would report <c>0</c> here, not <c>3</c> — every other fact touching
    /// <see cref="GraphNode.ElapsedAge"/> in this file compares it against zero or against itself, so this is
    /// the one that can actually fail on that class of bug.</para></summary>
    public static async Task Elapsed_age_advances_by_real_time_between_writes(
        IMemoryGraphStore store, string key, Action<TimeSpan> advance)
    {
        await store.UpsertAsync(Write("e", key, "the entry being aged"));
        advance(TimeSpan.FromDays(3));
        await store.UpsertAsync(Write("e", key, "a later write"));

        var hits = await store.SeedAsync("e", key, "s", "aged", 10);

        Assert.Equal(3, hits.Single().ElapsedAge, precision: 6);
    }

    /// <summary>The STRENGTH-side twin of <see cref="Elapsed_age_advances_by_real_time_between_writes"/>,
    /// and it was missing.
    /// <para><b>Why that mattered.</b> The only assertions on <see cref="GraphNode.StrengthElapsedAge"/> and
    /// <see cref="GraphNeighbour.EdgeElapsedAge"/> anywhere were <c>&gt;= 0</c>, plus one that positively
    /// REQUIRES <c>0</c> for an unconnected node — so a store hard-coding <c>0</c> for both satisfied every
    /// one of them. The encoding axis had this discriminating fact; the strength and edge axes got only the
    /// weak shape, which is the "a guard that cannot observe the thing it guards" trap.</para>
    /// <para><b>The consequence, which is why this is a contract fact and not a per-backend test.</b>
    /// <c>GraphMemoryEngine</c> projects these through whichever age policy is installed, and
    /// <c>ElapsedAgePolicy</c> is shipped. Under it, a constant zero means every edge reads as freshly
    /// strengthened forever: <c>GraphMemoryOptions.EdgeHalfLife</c> decays nothing and a connection boost
    /// never fades. That is <c>CLAUDE.md</c>'s own headline claim — "all THREE age axes now speak one unit"
    /// — resting on two axes nothing could observe. Found 2026-08-14.</para></summary>
    public static async Task Strength_elapsed_age_advances_by_real_time_between_strengthenings(
        IMemoryGraphStore store, string key, Action<TimeSpan> advance)
    {
        var a = await store.UpsertAsync(Write("e", key, "the connected entry being aged"));
        var b = await store.UpsertAsync(Write("e", key, "its neighbour across the edge"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: true);

        advance(TimeSpan.FromDays(3));
        // a later, UNRELATED write moves the clock's reference without touching the edge
        await store.UpsertAsync(Write("e", key, "a later unrelated write"));

        var node = await store.GetAsync("e", a);
        Assert.NotNull(node);
        Assert.Equal(3, node!.StrengthElapsedAge, precision: 6);

        var neighbours = await store.NeighboursAsync("e", key, [a], 10);
        Assert.Equal(3, neighbours.Single().EdgeElapsedAge, precision: 6);
    }

    /// <summary>A quiet engine must not age on ANY of the three primitives — the reason the totals are per
    /// engine, mirroring <see cref="A_busy_engine_does_not_age_a_quiet_ones_memories"/> for
    /// <see cref="GraphNode.Age"/>.</summary>
    public static async Task A_quiet_engine_does_not_age_on_any_of_the_three_primitives(
        IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("quiet", key, "a fact nobody disturbs"));
        await Crowd(store, "busy", key, 200);

        var hits = await store.SeedAsync("quiet", key, "s", null, 10);

        Assert.Single(hits);
        Assert.Equal(0, hits[0].OrdinalAge, precision: 6);
        Assert.Equal(0, hits[0].VolumeAge, precision: 6);
        Assert.Equal(0, hits[0].ElapsedAge, precision: 6);
    }

    public static async Task Touch_records_reinforcement(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "reinforced"));
        await Crowd(store, "e", key, 3);

        await store.TouchAsync("e", [new GraphTouch(id, 10.5)]);

        var node = await store.GetAsync("e", id);
        Assert.NotNull(node);
        Assert.Equal(10.5, node!.Stability, precision: 6);
        Assert.Equal(1, node.RecallCount);
        Assert.Equal(0, node.Age); // a touch stamps the CURRENT position, so the entry is fresh again
    }

    /// <summary>Recall must not age anything: a read-only agent never forgets by reading.</summary>
    public static async Task A_touch_does_not_advance_the_position(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "the touched one"));
        var other = await store.UpsertAsync(Write("e", key, "the untouched one"));

        var before = (await store.GetAsync("e", other))!.Age;
        await store.TouchAsync("e", [new GraphTouch(id, 10)]);
        var after = (await store.GetAsync("e", other))!.Age;

        Assert.Equal(before, after);
    }

    /// <summary>A touch resets a node's age on every scale AT ONCE — the same "stamp the CURRENT position"
    /// contract <see cref="Touch_records_reinforcement"/> pins for <see cref="GraphNode.Age"/>, now covering
    /// the three coexisting primitives too.</summary>
    public static async Task Touch_resets_all_three_primitives_together_with_the_legacy_position(
        IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "reinforced"));
        await Crowd(store, "e", key, 3);

        await store.TouchAsync("e", [new GraphTouch(id, 10.5)]);

        var node = await store.GetAsync("e", id);
        Assert.NotNull(node);
        Assert.Equal(0, node!.OrdinalAge, precision: 6);
        Assert.Equal(0, node.VolumeAge, precision: 6);
        Assert.Equal(0, node.ElapsedAge, precision: 6);
    }

    /// <summary>The other half of <see cref="A_touch_does_not_advance_the_position"/>: a recall must not age
    /// an UNTOUCHED node on any of the three primitives either, not only on the legacy position.</summary>
    public static async Task A_touch_does_not_advance_any_of_the_three_primitives(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "the touched one"));
        var other = await store.UpsertAsync(Write("e", key, "the untouched one"));

        var before = await store.GetAsync("e", other);
        await store.TouchAsync("e", [new GraphTouch(id, 10)]);
        var after = await store.GetAsync("e", other);

        Assert.NotNull(before);
        Assert.NotNull(after);
        Assert.Equal(before!.OrdinalAge, after!.OrdinalAge, precision: 6);
        Assert.Equal(before.VolumeAge, after.VolumeAge, precision: 6);
        Assert.Equal(before.ElapsedAge, after.ElapsedAge, precision: 6);
    }

    public static async Task Linked_nodes_are_reachable_as_neighbours(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        var b = await store.UpsertAsync(Write("e", key, "beta"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: true);

        var neighbours = await store.NeighboursAsync("e", key, [a], 10);

        Assert.Single(neighbours);
        Assert.Equal(b, neighbours[0].Node.Id);
    }

    public static async Task Linking_the_same_pair_again_strengthens_it(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        var b = await store.UpsertAsync(Write("e", key, "beta"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: false);
        await store.LinkAsync("e", a, b, null, 1, symmetric: false);

        var neighbours = await store.NeighboursAsync("e", key, [a], 10);

        Assert.Single(neighbours); // one edge, not two
        Assert.Equal(2, neighbours[0].EdgeWeight, precision: 6);
    }

    public static async Task An_edge_ages_as_the_memory_moves_on(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        var b = await store.UpsertAsync(Write("e", key, "beta"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: true);
        await Crowd(store, "e", key, 12);

        var neighbours = await store.NeighboursAsync("e", key, [a], 10);

        Assert.Equal(12, neighbours[0].EdgeAge, precision: 6);
    }

    public static async Task Degree_counts_connections(IMemoryGraphStore store, string key)
    {
        var hub = await store.UpsertAsync(Write("e", key, "hub"));
        foreach (var spoke in new[] { "one", "two", "three" })
        {
            var id = await store.UpsertAsync(Write("e", key, spoke));
            await store.LinkAsync("e", hub, id, null, 1, symmetric: true);
        }

        var node = await store.GetAsync("e", hub);

        Assert.Equal(3, node!.Degree);
    }

    public static async Task A_node_reports_its_connection_strength_and_freshness(
        IMemoryGraphStore store, string key)
    {
        var hub = await store.UpsertAsync(Write("e", key, "hub"));
        var one = await store.UpsertAsync(Write("e", key, "one"));
        var two = await store.UpsertAsync(Write("e", key, "two"));
        await store.LinkAsync("e", hub, one, null, 3, symmetric: false);
        await Crowd(store, "e", key, 5);
        await store.LinkAsync("e", hub, two, null, 2, symmetric: false);
        await Crowd(store, "e", key, 2);

        var node = await store.GetAsync("e", hub);

        // raw sum, and staleness measured from the MOST RECENT strengthening — the store applies no curve
        Assert.Equal(5, node!.Strength, precision: 6);
        Assert.Equal(2, node.StrengthAge, precision: 6);
    }

    /// <summary>The strength-side age primitives (design doc §5.7, 3.0 pre-freeze): a node reports how long
    /// since its freshest edge was strengthened on ALL THREE policy-independent scales, not only on the
    /// store's own <c>Advance</c>-driven position — the exact counterpart of
    /// <c>OrdinalAge</c>/<c>VolumeAge</c>/<c>ElapsedAge</c> for the encoding side.
    /// <para>The crowding writes below carry <c>advance: 40</c> precisely so the four scales cannot be
    /// confused for one another: three writes move the POSITION by 120 and the ORDINAL by 3. A backend that
    /// reported the position accumulator for the ordinal primitive — the whole defect this closes — reads 120
    /// where 3 is correct, so this fact fails rather than passing by coincidence.</para></summary>
    public static async Task A_node_reports_its_connection_freshness_on_every_age_scale(
        IMemoryGraphStore store, string key)
    {
        var hub = await store.UpsertAsync(Write("e", key, "hub"));
        var one = await store.UpsertAsync(Write("e", key, "one"));
        await store.LinkAsync("e", hub, one, null, 3, symmetric: false);

        // three writes, each declaring itself worth 40 positions — the two scales diverge by construction
        var chars = 0;
        for (var i = 0; i < 3; i++)
        {
            var content = $"crowding write number {i}";
            chars += content.Length;
            await store.UpsertAsync(new GraphNodeWrite("e", key, "filler", content, content,
                MemoryGrade.Associative, Stability, 40, null));
        }

        var node = await store.GetAsync("e", hub);
        Assert.NotNull(node);

        Assert.Equal(120, node!.StrengthAge, precision: 6);         // the Advance-driven accumulator
        Assert.Equal(3, node.StrengthOrdinalAge, precision: 6);      // one per write, whatever Advance said
        Assert.Equal(chars, node.StrengthVolumeAge, precision: 6);   // the crowding content's own length
        Assert.True(node.StrengthElapsedAge >= 0);                   // wall-clock, so only its sign is fixed
    }

    /// <summary>A NEIGHBOUR reports the connecting edge's own age on all three policy-independent scales too,
    /// not only on the store's <c>Advance</c>-driven position — the third and last age axis to stop reading
    /// the raw accumulator (3.0).
    /// <para>Same <c>advance: 40</c> construction as the node-side fact above, and for the same reason: three
    /// writes move the POSITION by 120 and the ORDINAL by 3, so a backend reporting the accumulator for the
    /// ordinal primitive reads 120 where 3 is correct.</para></summary>
    public static async Task A_neighbour_reports_its_edge_age_on_every_scale(IMemoryGraphStore store, string key)
    {
        var hub = await store.UpsertAsync(Write("e", key, "hub"));
        var spoke = await store.UpsertAsync(Write("e", key, "spoke"));
        await store.LinkAsync("e", hub, spoke, null, 3, symmetric: false);

        var chars = 0;
        for (var i = 0; i < 3; i++)
        {
            var content = $"crowding write number {i}";
            chars += content.Length;
            await store.UpsertAsync(new GraphNodeWrite("e", key, "filler", content, content,
                MemoryGrade.Associative, Stability, 40, null));
        }

        var neighbour = Assert.Single(await store.NeighboursAsync("e", key, [hub], 10));

        Assert.Equal(120, neighbour.EdgeAge, precision: 6);          // the Advance-driven accumulator
        Assert.Equal(3, neighbour.EdgeOrdinalAge, precision: 6);      // one per write, whatever Advance said
        Assert.Equal(chars, neighbour.EdgeVolumeAge, precision: 6);   // the crowding content's own length
        Assert.True(neighbour.EdgeElapsedAge >= 0);                   // wall-clock, so only its sign is fixed
    }

    /// <summary>An unconnected node has nothing to measure connection freshness FROM, so every strength scale
    /// reads zero — including the three primitives, whose SQL <c>MAX</c> over no rows is NULL and must not
    /// surface as one.</summary>
    public static async Task An_unconnected_node_reports_no_connection_freshness(
        IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "alone"));
        await Crowd(store, "e", key, 3);

        var node = await store.GetAsync("e", id);
        Assert.NotNull(node);

        Assert.Equal(0, node!.StrengthAge, precision: 6);
        Assert.Equal(0, node.StrengthOrdinalAge, precision: 6);
        Assert.Equal(0, node.StrengthVolumeAge, precision: 6);
        Assert.Equal(0, node.StrengthElapsedAge, precision: 6);
    }

    public static async Task An_unconnected_node_reports_no_strength(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "alone"));

        var node = await store.GetAsync("e", id);

        Assert.Equal(0, node!.Strength);
        Assert.Equal(0, node.StrengthAge);
    }

    public static async Task Prune_removes_only_what_it_is_told_to(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "faded note"));
        await store.UpsertAsync(Write("e", key, "exact note", MemoryGrade.Authoritative));
        await Crowd(store, "e", key, 40);

        var removed = await store.PruneAsync("e", key, "s", 4.32, null);

        Assert.Equal(1, removed); // the authoritative one is never eligible
        Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
    }

    /// <summary>Ordinals need only be MONOTONE, not dense: a deletion must leave a harmless gap, never force
    /// a renumbering. <see cref="GraphNode.OrdinalAge"/> is not "how many rows currently exist after me" — it
    /// is "how many writes have happened since me", and the two diverge the moment a row in between is gone.
    /// <para><b>Mutation target.</b> An implementation that derived <c>OrdinalAge</c> by counting SURVIVING
    /// rows (e.g. <c>COUNT(*) WHERE created_at &gt; mine</c>) instead of reading a per-engine counter would
    /// report 1 here (only "after" survives), not 6 — this fact is what catches that shortcut.</para></summary>
    public static async Task Ordinals_are_monotone_but_not_dense_after_a_prune(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "anchor", MemoryGrade.Authoritative)); // never eligible
        // 5 associative writes IN THE SAME SCOPE as anchor and the prune below — Crowd() writes to a
        // DIFFERENT scope ("filler") specifically so it does not compete with "s", which is exactly wrong
        // here: this fact needs rows PruneAsync's own scope filter can actually reach
        for (var i = 0; i < 5; i++) await store.UpsertAsync(Write("e", key, $"doomed {i}"));

        var removed = await store.PruneAsync("e", key, "s", -1, null); // any age at all, even zero, qualifies
        Assert.Equal(5, removed);

        await store.UpsertAsync(Write("e", key, "after"));

        var anchor = (await store.SeedAsync("e", key, "s", "anchor", 10)).Single();
        // 6 writes happened after "anchor" (5 doomed + "after"), even though 5 of those rows are gone
        Assert.Equal(6, anchor.OrdinalAge, precision: 6);
    }

    /// <summary>The precise counterpart to <see cref="Prune_removes_only_what_it_is_told_to"/> — a caller
    /// (2026-08-10 memory-policy-seams plan, Task 3 fix round 1, I-1: <c>GraphMemoryEngine.PruneAsync</c>,
    /// once ANY Derivable age policy is registered) that has already decided WHICH ids to remove, rather than
    /// asking the store to decide by ratio.</summary>
    public static async Task DeleteAsync_removes_exactly_the_given_ids(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        await store.UpsertAsync(Write("e", key, "beta"));
        var c = await store.UpsertAsync(Write("e", key, "gamma"));

        var removed = await store.DeleteAsync("e", [a, c]);

        Assert.Equal(2, removed);
        var survivor = Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
        Assert.Equal("beta", survivor.Content);
    }

    public static async Task DeleteAsync_takes_edges_with_the_nodes(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        var b = await store.UpsertAsync(Write("e", key, "beta"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: true);

        await store.DeleteAsync("e", [a]);

        var node = await store.GetAsync("e", b);
        Assert.NotNull(node);
        Assert.Equal(0, node!.Degree); // the edge to/from the deleted node did not survive as a dangling row
    }

    /// <summary>Scoped to the named engine, the same as every other member here — an id read from a
    /// DIFFERENT engine must not be removable through this one.</summary>
    public static async Task DeleteAsync_is_scoped_to_the_named_engine(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write("e", key, "mine"));

        var removed = await store.DeleteAsync("some-other-engine", [id]);

        Assert.Equal(0, removed);
        Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
    }

    public static async Task DeleteAsync_with_no_ids_removes_nothing(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "untouched"));

        var removed = await store.DeleteAsync("e", []);

        Assert.Equal(0, removed);
        Assert.Single(await store.SeedAsync("e", key, "s", null, 10));
    }

    public static async Task Forget_clears_a_scope(IMemoryGraphStore store, string key)
    {
        await store.UpsertAsync(Write("e", key, "gone"));

        await store.ForgetAsync("e", key, "s");

        Assert.Empty(await store.SeedAsync("e", key, "s", null, 10));
    }

    public static async Task Deleting_a_node_takes_its_edges_with_it(IMemoryGraphStore store, string key)
    {
        var a = await store.UpsertAsync(Write("e", key, "alpha"));
        var b = await store.UpsertAsync(Write("e", key, "beta"));
        await store.LinkAsync("e", a, b, null, 1, symmetric: true);

        await store.ForgetAsync("e", key, "s");
        await store.UpsertAsync(Write("e", key, "alpha")); // same content, new row

        var reborn = await store.SeedAsync("e", key, "s", "alpha", 10);
        Assert.Single(reborn);
        Assert.Equal(0, reborn[0].Degree); // no dangling edge survived
    }

    public static async Task Cancellation_propagates(IMemoryGraphStore store, string key)
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.SeedAsync("e", key, "s", null, 10, cts.Token));
    }

    // ---- the review log (2026-08-11, fsrs-properly plan Task 3): one row per reinforcement, carrying the
    // pre-review state, the derived grade, and the post-review state. Scoped by ENGINE alone, not by
    // task/scope like every fact above, so `key` is used as the engine name itself here — that is what keeps
    // these facts isolated from each other and from the review-free facts above when several run against
    // the SAME shared store (the Postgres suite runs every contract fact against one container). ----

    /// <summary>The write/read round trip <see cref="RecordReviewsAsync_evicts_down_to_the_cap"/> and the
    /// engine-level best-effort/no-feedback proofs both build on: every field written comes back exactly,
    /// including a NULL grade, and rows come back OLDEST FIRST.
    /// <para>The review log has TWO deliberately nullable columns — <c>grade</c> here and
    /// <c>verified</c> in <see cref="Reviews_round_trip_the_verified_tri_state"/>. They are nullable for
    /// unrelated reasons and are pinned separately.</para></summary>
    public static async Task Reviews_round_trip_through_the_store(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write(key, "t", "reviewed"));
        var batch = Guid.NewGuid();
        var graded = new MemoryReviewWrite(id, batch, PreAge: 12, PreStability: 20, PreDifficulty: 3,
            PreStrength: 5, PreStrengthAge: 2, Grade: 3.5, PostStability: 22, PostDifficulty: 3.2,
            ProvenanceRetrievability: 0x2);
        var ungraded = new MemoryReviewWrite(id, batch, PreAge: 0, PreStability: 22, PreDifficulty: 3.2,
            PreStrength: 0, PreStrengthAge: 0, Grade: null, PostStability: 22, PostDifficulty: 3.2,
            ProvenanceRetrievability: 0x2);

        await store.RecordReviewsAsync(key, [graded, ungraded], cap: 100);

        var rows = await store.ReviewsAsync(key);

        Assert.Equal(2, rows.Count);
        var first = rows[0];
        Assert.Equal(id, first.NodeId);
        Assert.Equal(batch, first.BatchId);
        Assert.Equal(12, first.PreAge, 9);
        Assert.Equal(20, first.PreStability, 9);
        Assert.Equal(3, first.PreDifficulty, 9);
        Assert.Equal(5, first.PreStrength, 9);
        Assert.Equal(2, first.PreStrengthAge, 9);
        Assert.NotNull(first.Grade);
        Assert.Equal(3.5, first.Grade!.Value, 9);
        Assert.Equal(22, first.PostStability, 9);
        Assert.Equal(3.2, first.PostDifficulty, 9);
        Assert.Equal(0x2, first.ProvenanceRetrievability);
        // the second row's own null grade — the one honest way to record "no grade-driven update happened
        // this reinforcement" (see IMemoryRetrievabilityPolicy.DerivedGrade's own remarks)
        Assert.Null(rows[1].Grade);
    }

    /// <summary><b>All THREE states of <c>verified</c> survive the round trip on every backend</b> —
    /// <c>true</c> (a judge said this answered the query), <c>false</c> (a judge said it did not), and
    /// <c>null</c> (no verifier ran).
    ///
    /// <para><b>Why this needs its own contract fact rather than an engine test.</b> The three states are
    /// load-bearing and not interchangeable: <c>false</c> is the observed failure that lets the log be
    /// FITTED at all (<c>docs/DECISIONS.md</c> <b>D51</b>/<b>D59</b>), while <c>null</c> is the absence of a
    /// judgement — collapsing them turns every deployment without a verifier into a corpus of fabricated
    /// failures. And the backends do not agree on how to store a boolean: Postgres has a native
    /// <c>BOOLEAN</c>, SQLite has none and round-trips through <c>1</c>/<c>0</c>/NULL with a hand-written
    /// mapping at the call site. A hand-written tri-state mapping that returns the WRONG state is precisely
    /// the class of defect that compiles, passes and ships.</para>
    ///
    /// <para>Distinguishes <c>false</c> from <c>null</c> explicitly rather than asserting "not true", since
    /// a mapping that folded both to null would satisfy the weaker check.</para></summary>
    public static async Task Reviews_round_trip_the_verified_tri_state(IMemoryGraphStore store, string key)
    {
        var id = await store.UpsertAsync(Write(key, "t", "judged"));
        var batch = Guid.NewGuid();
        MemoryReviewWrite Row(bool? verified) => new(id, batch, PreAge: 1, PreStability: 20,
            PreDifficulty: 3, PreStrength: 0, PreStrengthAge: 0, Grade: 3, PostStability: 20,
            PostDifficulty: 3, ProvenanceRetrievability: 0x2, Verified: verified);

        await store.RecordReviewsAsync(key, [Row(true), Row(false), Row(null)], cap: 100);

        var rows = await store.ReviewsAsync(key);

        Assert.Equal(3, rows.Count);
        Assert.True(rows[0].Verified);
        Assert.False(rows[1].Verified);          // NOT null — an observed failure, which is the useful one
        Assert.Null(rows[2].Verified);           // NOT false — no judgement happened
    }

    /// <summary>An empty batch logs nothing — the same "empty is a no-op" shape
    /// <see cref="DeleteAsync_with_no_ids_removes_nothing"/> pins for deletion.</summary>
    public static async Task RecordReviewsAsync_with_no_reviews_is_a_no_op(IMemoryGraphStore store, string key)
    {
        await store.RecordReviewsAsync(key, [], cap: 10);

        Assert.Empty(await store.ReviewsAsync(key));
    }

    /// <summary>Bounded by default (design spec §3): the cap ACTUALLY evicts, not merely exists as a number
    /// nobody enforces. <c>cap = 3</c> makes <see cref="Lyntai.Memory.MemoryReviewLogPacing.TrimInterval"/>
    /// its own floor of 1, so every single-row write in this fact crosses a trim boundary and the table can
    /// never exceed the cap even transiently — which is what turns this into an EXACT assertion (the newest
    /// 3 survive, nothing else) rather than merely a bound.
    /// <para><b>Mutation target.</b> An implementation that only ever appends, or that trims by an unrelated
    /// rule (oldest-first by wall-clock rather than by id, say), fails either the count or the survivor
    /// identity below.</para></summary>
    public static async Task RecordReviewsAsync_evicts_down_to_the_cap(IMemoryGraphStore store, string key)
    {
        const int cap = 3;
        for (var i = 0; i < 10; i++)
        {
            var review = new MemoryReviewWrite(NodeId: i, BatchId: Guid.NewGuid(), PreAge: i,
                PreStability: 20, PreDifficulty: 1, PreStrength: 0, PreStrengthAge: 0, Grade: null,
                PostStability: 20, PostDifficulty: 1);
            await store.RecordReviewsAsync(key, [review], cap);
        }

        var rows = await store.ReviewsAsync(key);

        Assert.Equal(cap, rows.Count);
        // oldest-first, and the newest `cap` writes (PreAge 7, 8, 9) are exactly the ones that survived
        Assert.Equal([7, 8, 9], rows.Select(r => (int)r.PreAge));
    }

    /// <summary>A stability UNDER the divide-by-zero floor is FLOORED, never substituted — on every backend.
    ///
    /// <para>The distinction is invisible at stability <c>0</c>, where both spellings give the same answer,
    /// and that is why it survived: <c>MAX(stability, 1e-6)</c> and <c>stability > 0 ? stability : 1e-6</c>
    /// agree on the value the guard was written for and disagree on every value strictly between zero and the
    /// floor. The relational backends floored; the in-process one substituted.</para>
    ///
    /// <para>Set up so the two answers differ in the OUTCOME rather than in a ratio nobody sees: stability
    /// <c>1e-7</c> at age 1 floors to <c>1/1e-6 = 1e6</c> (under the cutoff, kept) and substitutes to
    /// <c>1/1e-7 = 1e7</c> (over it, deleted). One <c>PruneAsync</c> call, same arguments, same data — and
    /// before this fact, the in-process store destroyed the entry the other two kept.</para>
    ///
    /// <para>A prune is a DELETE, so the two behaviours are not merely different: one of them loses a memory
    /// that no later call can bring back.</para></summary>
    public static async Task A_stability_under_the_floor_is_floored_not_substituted(
        IMemoryGraphStore store, string key)
    {
        const string engine = "floor";
        // stability far below the 1e-6 guard, then ONE unrelated write so the node's age is exactly 1
        await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", "faint", "faint",
            MemoryGrade.Associative, InitialStability: 1e-7, Advance: 1, Metadata: null));
        await Crowd(store, engine, key, 1);

        // 1e6 (floored) is under this cutoff; 1e7 (substituted) is over it
        var removed = await store.PruneAsync(engine, key, scope: null, maxAgeOverStability: 5e6,
            olderThan: null);

        Assert.Equal(0, removed);
    }

    /// <summary><b>An UNSTATED grade keeps the stored one; a stated grade overwrites it.</b>
    /// <para><see cref="GraphNodeWrite.GradeStated"/> exists because <c>MemoryGrade.Inherit</c> resolves to
    /// the engine's role before it ever reaches a store, so "the caller said nothing" and "the caller said
    /// Associative" used to arrive as the same value — and a re-remember that did not restate the grade
    /// silently demoted an authoritative fact, against design §5.7.0's objective (1).</para>
    /// <para><b>Both directions in one fact, because the rule is a distinction and not a prohibition.</b> A
    /// store that simply never updated the grade would pass the first half and break promotion, which is the
    /// capability the overwrite exists for.</para></summary>
    public static async Task An_unstated_grade_keeps_the_stored_one_and_a_stated_one_overwrites(
        IMemoryGraphStore store, string key)
    {
        const string engine = "grades";
        const string exact = "the recovery key is on the blue card";

        var id = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", exact, exact,
            MemoryGrade.Authoritative, Stability, 1, null));

        // UNSTATED: the caller left the grade to be resolved, so the stored one stands
        await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", exact, exact,
            MemoryGrade.Associative, Stability, 1, null, GradeStated: false));
        Assert.Equal(MemoryGrade.Authoritative, (await store.GetAsync(engine, id))!.Grade);

        // STATED: an explicit demotion is the caller's decision and must land
        await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", exact, exact,
            MemoryGrade.Associative, Stability, 1, null, GradeStated: true));
        Assert.Equal(MemoryGrade.Associative, (await store.GetAsync(engine, id))!.Grade);

        // ...and promotion, the capability the overwrite exists for
        await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", exact, exact,
            MemoryGrade.Authoritative, Stability, 1, null));
        Assert.Equal(MemoryGrade.Authoritative, (await store.GetAsync(engine, id))!.Grade);
    }

    /// <summary><b>An UNSTATED headline keeps the stored one; a stated headline overwrites it.</b> The same
    /// rule as <see cref="GraphNodeWrite.GradeStated"/>, on the same grounds and found the same way: the
    /// engine DERIVES a headline when the caller supplies none, so overwriting unconditionally replaced an
    /// author's own one-line summary with a truncation of the content.
    /// <para>Both directions, because correcting a headline has to keep working — a store that simply never
    /// updated it would pass the first half.</para></summary>
    public static async Task An_unstated_headline_keeps_the_stored_one_and_a_stated_one_overwrites(
        IMemoryGraphStore store, string key)
    {
        const string engine = "headlines";
        const string content = "the production database is db-prod-1 and reachable from the deploy subnet";

        var id = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", "prod DB", content,
            MemoryGrade.Associative, Stability, 1, null));

        // UNSTATED: the engine derived this one, so it must not displace the author's
        await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", content, content,
            MemoryGrade.Associative, Stability, 1, null, HeadlineStated: false));
        Assert.Equal("prod DB", (await store.GetAsync(engine, id))!.Headline);

        // STATED: an authored correction lands
        await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", "prod DB (subnet only)", content,
            MemoryGrade.Associative, Stability, 1, null));
        Assert.Equal("prod DB (subnet only)", (await store.GetAsync(engine, id))!.Headline);
    }

    /// <summary><b>No read crosses a <c>taskKey</c>.</b> The isolation every other guarantee is stated
    /// inside — "an authoritative fact is returned for a query it is relevant to" means nothing if the query
    /// can reach another tenant's scope.
    /// <para><b>Asserted across every task-scoped reader at once, because they are separate queries with
    /// separate predicates</b> and a missing one is a silent leak rather than an error. Seeding is checked
    /// three ways — with a query, with none (the enumeration path), and with a null scope, which is the
    /// reading that means "every scope of THIS task" and is where a dropped <c>task_key</c> would be least
    /// visible.</para></summary>
    public static async Task No_read_crosses_a_task_key(IMemoryGraphStore store, string key)
    {
        const string engine = "tenants";
        var other = key + "-other";

        var mine = await store.UpsertAsync(Write(engine, key, "the deploy pipeline needs approval"));
        await store.UpsertAsync(Write(engine, other, "the deploy pipeline needs approval"));
        await store.RecordSubjectsAsync(engine, mine, ["shared-handle"]);

        var matched = await store.SeedAsync(engine, key, "s", "deploy", 50);
        Assert.Equal([mine], matched.Select(n => n.Id).ToList());

        // no query: the ENUMERATION path, which has no match predicate to hide behind
        var enumerated = await store.SeedAsync(engine, key, "s", null, 50);
        Assert.Equal([mine], enumerated.Select(n => n.Id).ToList());

        // null scope means "every scope of THIS task", never "every task"
        var unscoped = await store.SeedAsync(engine, key, scope: null, query: null, limit: 50);
        Assert.Equal([mine], unscoped.Select(n => n.Id).ToList());

        // the subject index is a second address space onto the same rows, with its own predicate
        Assert.Empty(await store.NodesBySubjectAsync(engine, other, "s", "shared-handle", 50));
        Assert.DoesNotContain("shared-handle", await store.KnownSubjectsAsync(engine, other, "s", 50));
    }

    /// <summary><b>A removal never reaches another <c>taskKey</c> either</b> — the same isolation asserted on
    /// the verbs that DESTROY, where over-reach is the one direction a removal must never err in
    /// (<see cref="IPrunableMemory.PruneAsync"/>'s own remarks) and the damage is unrecoverable.</summary>
    public static async Task No_removal_crosses_a_task_key(IMemoryGraphStore store, string key)
    {
        const string engine = "tenants";
        var other = key + "-keep";

        await store.UpsertAsync(Write(engine, key, "the withdrawn fact about widgets"));
        var kept = await store.UpsertAsync(Write(engine, other, "the retained fact about widgets"));

        await store.ForgetAsync(engine, key, scope: null);
        Assert.Equal([kept], (await store.SeedAsync(engine, other, "s", null, 50)).Select(n => n.Id).ToList());

        await store.PruneAsync(engine, key, scope: null, maxAgeOverStability: -1, olderThan: null);
        Assert.Equal([kept], (await store.SeedAsync(engine, other, "s", null, 50)).Select(n => n.Id).ToList());
    }

    /// <summary><b><see cref="GraphNodeWrite.Metadata"/> round-trips, through BOTH readers.</b>
    /// <para>Untested until 2026-08-26: the contract passed <c>Metadata: null</c> at every one of its call
    /// sites, so "app-owned extra data" was persisted by three backends and asserted by none. It is the one
    /// open-ended field a caller can put anything in, which makes it the natural carrier for a prototype —
    /// and a prototype resting on an untested round-trip is resting on nothing.</para>
    /// <para>Both readers, because they are different queries: <see cref="IMemoryGraphStore.GetAsync"/>
    /// selects one row and <see cref="IMemoryGraphStore.SeedAsync"/> projects a candidate set, and a column
    /// missing from one projection is exactly the silent null <c>storage.md</c> warns about.</para></summary>
    public static async Task Metadata_round_trips_through_both_readers(IMemoryGraphStore store, string key)
    {
        const string engine = "meta";
        var metadata = new Dictionary<string, string>
        {
            ["lyntai.canonical_key"] = "project:phoenix:production-database",
            ["lyntai.valid_from"] = "2026-07-01T00:00:00Z",
        };

        var id = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s",
            "the production database is db-prod-1", "the production database is db-prod-1",
            MemoryGrade.Associative, Stability, 1, metadata));

        var byId = await store.GetAsync(engine, id);
        Assert.NotNull(byId!.Metadata);
        Assert.Equal("project:phoenix:production-database", byId.Metadata!["lyntai.canonical_key"]);
        Assert.Equal("2026-07-01T00:00:00Z", byId.Metadata["lyntai.valid_from"]);

        var seeded = Assert.Single(await store.SeedAsync(engine, key, "s", "production", 10));
        Assert.NotNull(seeded.Metadata);
        Assert.Equal("project:phoenix:production-database", seeded.Metadata!["lyntai.canonical_key"]);
    }

    /// <summary><b><see cref="GraphNodeWrite.Metadata"/> follows <see cref="GraphNodeWrite.Signals"/>' rule:
    /// an ABSENT bag keeps what is stored, a SUPPLIED bag replaces it.</b>
    ///
    /// <para>The two are the record's only open-ended, caller-owned dictionaries, and they now answer the
    /// same question the same way — <c>COALESCE(@incoming, stored)</c>, which was already written one line
    /// above for signals in every backend's upsert.</para>
    ///
    /// <para><b>Through 3.1.0 metadata was WRITE-ONCE</b>, and by omission rather than by design: it sat in
    /// the INSERT column list and was absent from <c>DO UPDATE SET</c>, where the neighbours that are
    /// deliberately absent (<c>stability</c>, <c>provenance_retrievability</c>) each carry a comment saying
    /// so and this one did not. The cost was silent — a caller correcting a mistyped source, or attaching
    /// anything it learned later, was ignored with no error and no way to tell, and the only route to a
    /// correction was delete-and-rewrite, which discards the node's id, its edges, its decay state and its
    /// subject links. <c>docs/DECISIONS.md</c> <b>D91</b>.</para>
    ///
    /// <para><b>REPLACE, not merge</b>, exactly as signals does: a supplied bag is the caller's whole
    /// opinion, so keys it does not restate are gone. Merging would make removing a key impossible, which is
    /// the mirror of the defect this fixes.</para></summary>
    public static async Task Metadata_keeps_the_stored_bag_when_none_is_supplied_and_replaces_it_when_one_is(
        IMemoryGraphStore store, string key)
    {
        const string engine = "meta";
        const string content = "the production database is db-prod-1";

        var first = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", content, content,
            MemoryGrade.Associative, Stability, 1,
            new Dictionary<string, string> { ["source"] = "runbook-v1", ["stale"] = "yes" }));

        // ABSENT: no opinion, so the stored bag stands
        var second = await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", content, content,
            MemoryGrade.Associative, Stability, 1, null));
        Assert.Equal(first, second);   // the same row — a re-remember, not a second fact
        Assert.Equal("runbook-v1", (await store.GetAsync(engine, first))!.Metadata!["source"]);

        // SUPPLIED: the caller's whole opinion replaces it, so `stale` is gone rather than merged
        await store.UpsertAsync(new GraphNodeWrite(engine, key, "s", content, content,
            MemoryGrade.Associative, Stability, 1,
            new Dictionary<string, string> { ["source"] = "runbook-v2" }));

        var node = await store.GetAsync(engine, first);
        Assert.Equal("runbook-v2", node!.Metadata!["source"]);
        Assert.DoesNotContain("stale", node.Metadata.Keys);
    }
}
