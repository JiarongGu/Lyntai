namespace Lyntai.Tests.Memory.Corpus;

using Lyntai.Memory;
using Lyntai.Memory.Forgetting;
using Shape = CorpusShape;

/// <summary>
/// Proves <see cref="MemoryCorpus"/> produces what it CLAIMS, because every later measurement in this plan
/// depends on it: a sweep over a corpus whose ground truth is wrong reports the wrong winner, and a sweep
/// over a corpus that ignores one of its own swept parameters reports a whole column of meaningless noise.
/// <para>Two determinism facts, one per direction — same seed ⇒ identical corpus is necessary but not
/// sufficient, because a generator that ignores its seed entirely passes that half perfectly. Then one fact
/// per declared class, and one fact per swept parameter proving it actually moves the corpus the way its
/// name implies.</para>
/// <para>Every fact below derives writes/queries from <c>corpus.Steps</c> directly
/// (<c>Steps.OfType&lt;CorpusWrite&gt;()</c> / <c>Steps.OfType&lt;CorpusQuery&gt;()</c>) rather than through
/// a shared top-level helper — deliberately, so nothing in this file teaches the shape of a two-phase
/// (write-everything-then-query-everything) consumer. See <see cref="MemoryCorpus"/>'s own doc comment for
/// the ordering contract this is protecting.</para>
/// <para><b>A second family, added 2026-08-10 (DSR-default falsification plan Task 1 / TASKS.md Part 55):
/// proves the corpus actually reaches the DISCRIMINATING regime, and that neither curve collapses to a
/// shared boundary once it does</b> — the whole reason for that retarget (see <see cref="MemoryCorpus"/>'s
/// own class doc, "this corpus is an INSTRUMENT, not a simulation"). These guards are PROPERTY-BASED over
/// <see cref="Grid"/>, the SAME 60-shape grid <see cref="No_reuse_query_occurs_at_age_zero"/> already used —
/// not a hand-picked shape, which is the defect this file's own history records recurring twice already
/// (<see cref="Topical_reuse_queries_reach_the_discriminating_bands_ceiling"/> and
/// <see cref="Hot_ephemeral_in_window_queries_reach_the_discriminating_bands_floor"/> each replace a
/// single-shape predecessor that made exactly that mistake).</para>
/// </summary>
public class MemoryCorpusTests
{
    // A helper, not a magic number: entry ids are always written as " {id} " inside their content/query
    // text (a leading AND trailing space), so this is the one place a substring check can't confuse
    // "topic1" with "topic10" or "critical1" with "critical10".
    private static bool Mentions(string text, string id) => text.Contains($" {id} ", StringComparison.Ordinal);

    // Every corpus entry's content starts "item {id} …" (see MemoryCorpus's own doc), so the id is always
    // the second space-delimited token. Mirrors MemoryPolicySweep.ExtractCorpusId deliberately — two
    // independent readers of the same convention, not because either derives from the other.
    private static string ExtractId(string content) => content.Split(' ')[1];

    // ---- AttributeCount: the subject-cued attribute cluster (2026-08-12) ----

    /// <summary>Every language, ENUMERATED rather than listed — so adding one automatically inherits every
    /// invariant below instead of silently shipping unguarded. A hardcoded list is how a new arm gets
    /// measured without ever being checked, which is the failure this whole file exists to prevent.</summary>
    public static TheoryData<CorpusLanguage> Languages() => [.. Enum.GetValues<CorpusLanguage>()];

    /// <summary>Every language except the default — for facts about what a NON-English arm must satisfy.</summary>
    public static TheoryData<CorpusLanguage> NonEnglishLanguages() =>
        [.. Enum.GetValues<CorpusLanguage>().Where(l => l != CorpusLanguage.English)];

    private static IEnumerable<string> AttributeContent(MemoryCorpus corpus, CorpusLexicon lex) =>
        corpus.Steps.OfType<CorpusWrite>().Select(w => w.Write.Content)
            .Where(c => c.StartsWith($"{lex.ItemToken} attribute", StringComparison.Ordinal));

    private static List<CorpusQuery> AttributeCues(MemoryCorpus corpus, CorpusLexicon lex) =>
        [.. corpus.Steps.OfType<CorpusQuery>().Where(q => lex.IsAttributeCue(q.Text))];

    /// <summary><b>The authoritative probe must share no DISTINCTIVE term with the facts it is supposed to
    /// return</b> — nothing beyond the shared leading token every entry carries by construction.
    /// <para>This is what makes objective (1) measurable. The class asks "does an exact fact come back when
    /// nothing SINGLES IT OUT lexically", so a probe that happened to match those entries distinctively would
    /// turn the measurement into an ordinary keyword recall and report the engine's only no-failure-rate
    /// promise as KEPT when it was never tested.</para>
    /// <para><b>The leading token is excluded deliberately, not conveniently.</b> Every queried entry shares
    /// it — that is the corpus's whole competition property — so "shares no term at all" is unachievable for
    /// any query here, and a guard demanding it would be unsatisfiable rather than strict. What it buys is
    /// still the right thing: the authoritative facts sit in the candidate set on exactly the same weak
    /// footing as ~260 others, so under a limit of 10 only the grade carve-out can bring them
    /// back.</para></summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void The_authoritative_probe_shares_no_distinctive_term_with_the_facts_it_must_return(
        CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AuthoritativeCount = 2, Language = language }, seed: 909);

        var facts = corpus.Steps.OfType<CorpusWrite>()
            .Select(w => w.Write.Content)
            .Where(c => ExtractId(c).StartsWith("authoritative", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(2, facts.Count);

        var shared = lex.LiveTermsOf(lex.ItemToken).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var distinctive = lex.LiveTermsOf(lex.UnrelatedProbe()).Where(t => !shared.Contains(t)).ToList();
        Assert.NotEmpty(distinctive);   // a probe of nothing but the shared token would be vacuous

        foreach (var term in distinctive)
            Assert.DoesNotContain(facts, f => f.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The headline-only class really does hide its marker from its own CONTENT — which is the
    /// entire instrument. Every other class lets the engine DERIVE a headline from content, so headline
    /// words are a subset of content words and a query matching one matches the other; a marker that leaked
    /// into content would turn this into an ordinary content recall and report headline search as working
    /// when it was never exercised. The same failure mode
    /// <see cref="The_authoritative_probe_shares_no_distinctive_term_with_the_facts_it_must_return"/>
    /// guards for the grade carve-out.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void A_headline_only_entry_hides_its_marker_from_its_own_content(CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { HeadlineOnlyCount = 2, Language = language }, seed: 909);

        var entries = corpus.Steps.OfType<CorpusWrite>()
            .Where(w => w.Write.Headline is not null)
            .ToList();
        Assert.Equal(2, entries.Count);

        foreach (var entry in entries)
        {
            Assert.Contains(lex.HeadlineMarker, entry.Write.Headline!, StringComparison.Ordinal);
            // THE INVARIANT: nowhere in the content, so only a headline search can answer the probe.
            Assert.DoesNotContain(lex.HeadlineMarker, entry.Write.Content, StringComparison.Ordinal);
        }

        // …and in no OTHER entry's content either, or an unrelated write would answer the probe.
        Assert.DoesNotContain(corpus.Steps.OfType<CorpusWrite>(),
            w => w.Write.Content.Contains(lex.HeadlineMarker, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void The_headline_probe_is_emitted_and_names_exactly_the_headline_only_entries(
        CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { HeadlineOnlyCount = 3, Language = language }, seed: 909);

        var probe = Assert.Single(corpus.Steps.OfType<CorpusQuery>(), q => q.Text == lex.HeadlineMarker);

        Assert.Equal(["headline0", "headline1", "headline2"], probe.RelevantIds);
        // LAST, so the gap between the writes and the probe is the whole rest of the corpus — a hit is
        // retrieval rather than freshness, the same reason critical-rare's probe is emitted at the end.
        Assert.Same(probe, corpus.Steps.OfType<CorpusQuery>().Last());
    }

    [Fact]
    public void The_headline_axis_is_OPT_IN_so_an_unset_shape_authors_no_headline_at_all()
    {
        // Byte-identity with every existing measurement is what makes this safe to add, and it is the same
        // guarantee AuthoritativeCount carries. The goldens prove the corpus is unchanged; this states WHY.
        var corpus = MemoryCorpus.Generate(CorpusShape.Default, seed: 909);

        Assert.DoesNotContain(corpus.Steps.OfType<CorpusWrite>(), w => w.Write.Headline is not null);
    }

    /// <summary>Authoritative entries really are written at the GRADE — the corpus held zero
    /// <see cref="MemoryGrade"/> references before this class existed, so objective (1) was structurally
    /// unmeasurable. A class that wrote them as ordinary material would measure nothing while looking
    /// identical.</summary>
    [Fact]
    public void Authoritative_entries_are_written_at_the_authoritative_grade()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default with { AuthoritativeCount = 2 }, seed: 909);

        var graded = corpus.Steps.OfType<CorpusWrite>()
            .Where(w => ExtractId(w.Write.Content).StartsWith("authoritative", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(2, graded.Count);
        Assert.All(graded, w => Assert.Equal(MemoryGrade.Authoritative, w.Write.Grade));
        // …and nothing else is graded, so a miss on any other class cannot be excused by admission
        Assert.All(corpus.Steps.OfType<CorpusWrite>().Except(graded),
            w => Assert.Equal(MemoryGrade.Inherit, w.Write.Grade));
    }

    /// <summary>Opt-in, and the default shape is untouched — same guarantee, same reasoning as
    /// <see cref="ExpandRatio_defaults_to_zero_and_changes_nothing"/>.</summary>
    [Fact]
    public void AuthoritativeCount_defaults_to_zero_and_changes_nothing()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default, seed: 4242);

        Assert.Equal(0, CorpusShape.Default.AuthoritativeCount);
        Assert.DoesNotContain(corpus.Steps.OfType<CorpusWrite>(),
            w => ExtractId(w.Write.Content).StartsWith("authoritative", StringComparison.Ordinal));

        var explicitly = MemoryCorpus.Generate(
            new CorpusShape(ReuseRatio: 4, NoiseDensity: 8, CriticalRarity: 6, CandidateCount: 10), seed: 4242);
        Assert.Equal(Describe(explicitly.Steps), Describe(corpus.Steps));
    }

    /// <summary>Opt-in, and the default shape is untouched — same guarantee, same reasoning as
    /// <see cref="ExpandRatio_defaults_to_zero_and_changes_nothing"/>.</summary>
    [Fact]
    public void AttributeCount_defaults_to_zero_and_changes_nothing()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default, seed: 909);

        Assert.Equal(0, CorpusShape.Default.AttributeCount);
        Assert.DoesNotContain(corpus.Steps, s =>
            s is CorpusQuery q && q.Text.EndsWith(" recallcue", StringComparison.Ordinal));
        Assert.Equal(0, CorpusShape.Default.AttributeCount);

        var explicitly = MemoryCorpus.Generate(
            new CorpusShape(ReuseRatio: 4, NoiseDensity: 8, CriticalRarity: 6, CandidateCount: 10), seed: 909);
        Assert.Equal(Describe(explicitly.Steps), Describe(corpus.Steps));
    }

    /// <summary><b>The language axis is opt-in and English is the default</b> — the same additive guarantee
    /// every other axis here carries, and the one <c>MemoryCorpusGoldenTests</c> proves byte-exactly.</summary>
    [Fact]
    public void Language_defaults_to_english_and_changes_nothing()
    {
        Assert.Equal(CorpusLanguage.English, CorpusShape.Default.Language);

        var corpus = MemoryCorpus.Generate(CorpusShape.Default, seed: 909);
        var explicitly = MemoryCorpus.Generate(
            CorpusShape.Default with { Language = CorpusLanguage.English }, seed: 909);

        Assert.Equal(Describe(explicitly.Steps), Describe(corpus.Steps));
    }

    /// <summary><b>The two languages produce STRUCTURALLY IDENTICAL timelines — same step kinds in the same
    /// order, same ids, same ground-truth sets — differing only in text.</b>
    /// <para>This is what licenses a paired English-vs-Chinese measurement. Without it, a difference in
    /// recall quality could be a difference in the timelines (more writes, different interference, a
    /// different ground truth) rather than in the language, and the comparison would be uninterpretable.
    /// It holds because the generator's control flow depends only on <see cref="CorpusShape"/> and the seed,
    /// and because every filler list is the same LENGTH — so the seeded PRNG advances identically and the
    /// timelines stay in lockstep. That last part is a real constraint on adding a language, which is why it
    /// is pinned rather than assumed, and why
    /// <see cref="Every_lexicon_draws_from_the_same_number_of_fillers"/> sits beside it to name the cause
    /// instead of leaving a future author to rediscover it from a skeleton mismatch.</para></summary>
    [Theory]
    [MemberData(nameof(NonEnglishLanguages))]
    public void Every_language_produces_a_timeline_structurally_identical_to_english(CorpusLanguage language)
    {
        var shape = CorpusShape.Default with { AttributeCount = 3, ExpandRatio = 2 };
        var english = MemoryCorpus.Generate(shape, seed: 909);
        var chinese = MemoryCorpus.Generate(shape with { Language = language }, seed: 909);

        static List<string> Skeleton(MemoryCorpus c) =>
            [.. c.Steps.Select(s => s switch
            {
                // ids only — the text is exactly what is allowed to differ
                CorpusWrite w => $"W:{w.Write.Content.Split(' ')[1]}",
                CorpusQuery q => $"Q:{string.Join(",", q.RelevantIds)}",
                CorpusExpand e => $"E:{e.EntryId}",
                _ => throw new InvalidOperationException($"unhandled step {s.GetType().Name}"),
            })];

        Assert.Equal(Skeleton(english), Skeleton(chinese));
        Assert.NotEqual(Describe(english.Steps), Describe(chinese.Steps));   // …and the TEXT really does differ
    }

    /// <summary><b>The shared leading token SURVIVES the term floor and therefore actually makes entries
    /// compete — in every language.</b>
    /// <para>This is the corpus's central design property ("without that shared term nothing here would be
    /// measuring anything, because nothing would be fighting for the same ranked slots" — see
    /// <see cref="MemoryCorpus"/>) and it was silently broken the first time Chinese was added: the token was
    /// 条目, TWO characters, below <see cref="Lyntai.Storage.SearchTerms.MinimumTermLength"/>, so it yielded
    /// no trigram and was dropped from every query. Nothing failed. The measurement simply became easier —
    /// the first Chinese sweep reported <c>topical</c> miss AND pollution of exactly 0.0000 on almost every
    /// shape, which reads like a language finding and is an instrument that stopped measuring.</para>
    /// <para>Asserted through <c>SearchTerms</c> on BOTH sides, because that is the split the store performs:
    /// a term of the leading token must survive in a query AND be present in unrelated content. Checking the
    /// literal token instead would pass on a two-character token that never reaches the index.</para></summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void The_shared_leading_token_survives_the_term_floor_and_reaches_unrelated_entries(
        CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AttributeCount = 3, Language = language }, seed: 909);

        var sharedTerms = Lyntai.Storage.SearchTerms.Extract(lex.ItemToken);
        Assert.NotEmpty(sharedTerms);   // the token itself must clear the floor

        // a topical reuse query is the ordinary case: it must carry at least one shared term …
        var query = corpus.Steps.OfType<CorpusQuery>()
            .First(q => q.Text.Contains("topic", StringComparison.Ordinal));
        var queryTerms = Lyntai.Storage.SearchTerms.Extract(query.Text).ToHashSet(StringComparer.Ordinal);
        var carried = sharedTerms.Where(queryTerms.Contains).ToList();
        Assert.NotEmpty(carried);

        // … and that term must reach entries the query is NOT about, which is what "compete" means here
        var unrelated = corpus.Steps.OfType<CorpusWrite>()
            .Select(w => w.Write.Content)
            .Where(c => ExtractId(c).StartsWith("critical", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(unrelated);
        Assert.All(carried, term =>
            Assert.Contains(unrelated, c => c.Contains(term, StringComparison.Ordinal)));
    }

    /// <summary>The mechanism behind the structural-identity fact above, asserted directly so a future
    /// language fails HERE with a readable reason rather than there with an unexplained skeleton mismatch. One
    /// filler is drawn per entry from the seeded PRNG; a list of a different length consumes the same draw but
    /// lands elsewhere, and every downstream count silently diverges.</summary>
    [Fact]
    public void Every_lexicon_draws_from_the_same_number_of_fillers()
    {
        var counts = Enum.GetValues<CorpusLanguage>()
            .Select(l => CorpusLexicon.For(l).Fillers.Count)
            .Distinct()
            .ToList();

        Assert.Single(counts);
    }

    /// <summary><b>A SPACELESS language's content is ONE run apart from the ASCII id.</b> Without this a
    /// variant could silently degrade into the English case — a stray space would let whitespace splitting do
    /// the work and the trigram path, the entire reason this axis exists, would go unexercised while every
    /// test still passed.
    /// <para>Driven by <see cref="CorpusLexicon.WritesWordSpaces"/> rather than a hard-coded language list,
    /// because <b>Korean writes spaces</b>: demanding three parts of it would be meaningless, and a test
    /// relaxed to accommodate it would stop checking the thing it exists for. Korean's own version of this
    /// guarantee is <see cref="Every_non_english_arm_exercises_the_trigram_expansion"/>.</para></summary>
    [Theory]
    [MemberData(nameof(NonEnglishLanguages))]
    public void A_spaceless_languages_content_is_one_run_apart_from_the_id(CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        if (lex.WritesWordSpaces) return;   // see WritesWordSpaces — Korean is covered by the fact below

        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AttributeCount = 3, Language = language }, seed: 909);

        foreach (var content in corpus.Steps.OfType<CorpusWrite>().Select(w => w.Write.Content))
        {
            var parts = content.Split(' ');
            Assert.Equal(3, parts.Length);              // "{leading} {id} {one spaceless run}"
            Assert.True(parts[2].Length >= 3, $"body too short to yield a trigram: {content}");
        }
    }

    /// <summary><b>Every non-English arm actually reaches the trigram expansion.</b> The complement of the
    /// fact above and the one that covers Korean: Korean writes spaces so it cannot be asserted spaceless, but
    /// its tokens must still be long enough for <c>SearchTerms</c> to expand them — otherwise the arm would be
    /// exercising plain whole-word matching and reporting it as a CJK measurement.
    /// <para>Checked on an attribute CUE rather than any convenient step, because the cue is the query the
    /// whole cluster measurement turns on.</para></summary>
    [Theory]
    [MemberData(nameof(NonEnglishLanguages))]
    public void Every_non_english_arm_exercises_the_trigram_expansion(CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AttributeCount = 3, Language = language }, seed: 909);

        var terms = lex.LiveTermsOf(AttributeCues(corpus, lex)[0].Text);

        Assert.NotEmpty(terms);
        // a three-character term drawn from the language's own (non-ASCII) script — i.e. a real trigram
        Assert.Contains(terms, t => t.Length == 3 && t.Any(c => c > '⿿'));
    }

    /// <summary><b>Under <see cref="AttributeCueKind.Discriminative"/> the cue's ONLY live search token is the
    /// subject</b> — which is what makes miss = <c>1 - 1/AttributeCount</c> a clean no-graph floor and the
    /// number interpretable.
    /// <para>Pinned as "no query token appears in any NON-cluster entry's content", the property rather than
    /// the wording, because the first draft violated it while reading perfectly reasonable ("remind me about
    /// the {subject}" — <c>"the"</c> is a live token under <c>FtsQuery</c>'s three-character rule). A test
    /// checking the text would not have caught it.</para>
    /// <para><b>The overlapping form is NOT a bug and has its own fact below.</b> It measures the same
    /// question under the contention non-Latin content faces by construction, since this store tokenizes FTS
    /// as trigram and almost any two texts share trigrams.</para></summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void A_discriminative_attribute_query_shares_no_token_with_material_outside_its_cluster(
        CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AttributeCount = 3, Language = language }, seed: 909);

        var outsideContent = corpus.Steps.OfType<CorpusWrite>()
            .Select(w => w.Write.Content)
            .Where(c => !c.StartsWith($"{lex.ItemToken} attribute", StringComparison.Ordinal))
            .ToList();

        var cues = AttributeCues(corpus, lex);
        Assert.NotEmpty(cues);

        // LiveTermsOf is SearchTerms — the store's own split, so this asks the question the store will ask:
        // words in English, character trigrams in Chinese. Checking English word-splitting against Chinese
        // text would pass vacuously, which is exactly how a variant goes unguarded.
        foreach (var q in cues)
            foreach (var term in lex.LiveTermsOf(q.Text))
                Assert.DoesNotContain(outsideContent,
                    c => c.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary><b>A discriminative cue must reach EXACTLY ONE cluster member — not zero, not all three.</b>
    /// <para>The existing facts pin the upper bound (it shares no term with material OUTSIDE the cluster) and
    /// nothing pinned the lower one, so a cue that matched NOTHING passed every check. That is what happened
    /// to the first Chinese lexicon: the subject 配偶 is TWO characters, below the trigram floor, and gluing
    /// it to the marker produced trigrams (配偶回, 偶回忆, …) that straddle the boundary and appear in no
    /// entry at all. The arm dutifully reported <c>attribute</c> miss ≈ 0.889 with pollution 0.000 — a cue
    /// returning almost nothing — against English's 0.299, and that gap reads as a language finding when it
    /// is two different experiments.</para>
    /// <para>Exactly one is what makes <c>miss = 1 - 1/AttributeCount</c> the no-graph floor and the number
    /// interpretable: the other members can arrive ONLY through the graph. Zero makes the floor 1.0 and
    /// measures nothing; more than one lowers the floor silently and flatters the graph.</para></summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void A_discriminative_cue_reaches_exactly_one_cluster_member(CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AttributeCount = 3, Language = language }, seed: 909);

        var clusterContent = AttributeContent(corpus, lex).ToList();
        Assert.Equal(3, clusterContent.Count);

        foreach (var q in AttributeCues(corpus, lex))
        {
            var terms = lex.LiveTermsOf(q.Text);
            var reached = clusterContent
                .Count(c => terms.Any(t => c.Contains(t, StringComparison.OrdinalIgnoreCase)));

            Assert.True(reached == 1,
                $"[{language}] cue '{q.Text}' lexically reaches {reached} of 3 cluster members; exactly 1 is " +
                "what makes miss = 1 - 1/AttributeCount the no-graph floor. Terms: " +
                string.Join(", ", terms));
        }
    }

    /// <summary><b>And the overlapping form genuinely DOES contend with unrelated material</b> — otherwise
    /// the two cue kinds would differ only in wording and any gap between their measurements would be noise
    /// rather than the effect being studied.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void An_overlapping_attribute_query_shares_a_token_with_material_outside_its_cluster(
        CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with
            {
                AttributeCount = 3, AttributeCue = AttributeCueKind.SharesCommonTokens, Language = language,
            },
            seed: 909);

        var outsideContent = corpus.Steps.OfType<CorpusWrite>()
            .Select(w => w.Write.Content)
            .Where(c => !c.StartsWith($"{lex.ItemToken} attribute", StringComparison.Ordinal))
            .ToList();

        var query = AttributeCues(corpus, lex)[0];
        var terms = lex.LiveTermsOf(query.Text);

        // at least one live term reaches outside the cluster — that is what "contends" means
        Assert.Contains(terms,
            term => outsideContent.Any(c => c.Contains(term, StringComparison.OrdinalIgnoreCase)));

        // …and it still reaches its OWN cluster. Same hole as the discriminative cue's: contending with
        // unrelated material while matching none of the material it is about is not a harder version of the
        // question, it is a different one. Checked here because a cue is easy to word so it hits everything
        // EXCEPT the thing it names.
        Assert.Contains(AttributeContent(corpus, lex),
            c => terms.Any(t => c.Contains(t, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary><b>The cue NEVER contains its own answer.</b> The property that makes this class a different
    /// question from every other one here, and the one a careless edit to the query text would silently
    /// destroy — leaving a class that looks like it tests partial-cue recall and actually tests the same
    /// full-cue lookup as `critical-rare`.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void An_attribute_query_never_contains_the_entry_id_or_its_value(CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AttributeCount = 3, Language = language }, seed: 909);

        var contentById = AttributeContent(corpus, lex).ToDictionary(ExtractId, c => c, StringComparer.Ordinal);
        Assert.Equal(3, contentById.Count);

        var queries = AttributeCues(corpus, lex);
        Assert.NotEmpty(queries);

        foreach (var q in queries)
            foreach (var (id, content) in contentById)
            {
                Assert.DoesNotContain(id, q.Text, StringComparison.Ordinal);

                // the VALUE — the token the query is supposed to make the engine recover. Parsed by the
                // lexicon, because "the word after `is`" is English grammar and would return something
                // plausible and WRONG on Chinese rather than failing loudly.
                Assert.DoesNotContain(lex.AttributeValueOf(content), q.Text, StringComparison.Ordinal);
            }
    }

    /// <summary><b>The lexicon's own value reader is correct</b> — asserted directly, because every "the cue
    /// never contains its answer" fact above is only as good as the parse behind it. A reader that returned
    /// the empty string, or the filler word instead of the name, would make those facts pass vacuously.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void The_lexicon_reads_back_the_value_it_wrote(CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AttributeCount = 3, Language = language }, seed: 909);

        var values = AttributeContent(corpus, lex).Select(lex.AttributeValueOf).ToList();

        Assert.Equal(3, values.Count);
        Assert.All(values, v => Assert.Contains(v, lex.AttributeValues));
        Assert.Equal(3, values.Distinct(StringComparer.Ordinal).Count());   // three DIFFERENT values
    }

    /// <summary><b>Every attribute query declares the WHOLE cluster relevant, not just the fact it names.</b>
    /// This is the class's whole assertion — "even if I don't mention my wife, this entire relationship of
    /// mine should stay relevant" — so a change that narrowed ground truth to the lexically-matched entry
    /// would turn a test of the GRAPH into a test of the index, and would do it silently.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void An_attribute_query_declares_the_whole_cluster_relevant(CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AttributeCount = 3, Language = language }, seed: 909);

        var allAttributeIds = corpus.Steps.OfType<CorpusWrite>()
            .Select(w => ExtractId(w.Write.Content))
            .Where(id => id.StartsWith("attribute", StringComparison.Ordinal))
            .ToHashSet(StringComparer.Ordinal);
        Assert.Equal(3, allAttributeIds.Count);

        var cues = AttributeCues(corpus, lex);
        Assert.NotEmpty(cues);

        foreach (var q in cues)
            Assert.Equal(allAttributeIds.OrderBy(x => x, StringComparer.Ordinal),
                q.RelevantIds.OrderBy(x => x, StringComparer.Ordinal));
    }

    // ---- ExpandRatio: the opt-in expansion axis (2026-08-12, TASKS.md Part 64) ----

    /// <summary><b>The guarantee the whole axis rests on: at the default <c>ExpandRatio = 0</c>, a corpus is
    /// byte-identical to one generated before the axis existed.</b> Asserted as the WRITE-AND-QUERY SEQUENCE
    /// rather than as "no expansions", because the weaker claim would still pass if adding the axis had
    /// perturbed the rng draw or the filler count — which would move every published measurement at once
    /// while each individual pin merely looked freshly re-baselined.</summary>
    [Fact]
    public void ExpandRatio_defaults_to_zero_and_changes_nothing()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default, seed: 4242);

        Assert.Equal(0, CorpusShape.Default.ExpandRatio);
        Assert.DoesNotContain(corpus.Steps, s => s is CorpusExpand);

        // the identical shape written out longhand, i.e. what a caller before this axis existed produced
        var explicitly = MemoryCorpus.Generate(
            new CorpusShape(ReuseRatio: 4, NoiseDensity: 8, CriticalRarity: 6, CandidateCount: 10), seed: 4242);

        Assert.Equal(Describe(explicitly.Steps), Describe(corpus.Steps));
    }

    /// <summary>Raising it produces expansions, and every one names an entry the query it follows declared
    /// RELEVANT — the property that makes it a usefulness oracle rather than a random touch.</summary>
    [Fact]
    public void Raising_ExpandRatio_expands_only_entries_declared_relevant()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default with { ExpandRatio = 2 }, seed: 4242);

        var expansions = corpus.Steps.OfType<CorpusExpand>().ToList();
        Assert.NotEmpty(expansions);

        for (var i = 0; i < corpus.Steps.Count; i++)
        {
            if (corpus.Steps[i] is not CorpusExpand e) continue;

            // an expansion always FOLLOWS the query it belongs to — a consumer opens what it has just seen
            Assert.True(i > 0, "an expansion cannot be the first step: it follows the query that surfaced it");
            var previous = Assert.IsType<CorpusQuery>(corpus.Steps[i - 1]);
            Assert.Contains(e.EntryId, previous.RelevantIds);
        }
    }

    /// <summary><b>Expansions are ADDITIVE to the timeline: they never displace or reorder a write or a
    /// query.</b> Without this, an expansion-enabled corpus would be measuring a different corpus as well as
    /// a different engine behaviour, and no comparison against the shipped arm would mean anything.</summary>
    [Fact]
    public void Expansions_leave_the_write_and_query_sequence_untouched()
    {
        var without = MemoryCorpus.Generate(CorpusShape.Default, seed: 4242);
        var with = MemoryCorpus.Generate(CorpusShape.Default with { ExpandRatio = 2 }, seed: 4242);

        Assert.Contains(with.Steps, s => s is CorpusExpand);
        Assert.Equal(Describe(without.Steps), Describe(with.Steps));
    }

    /// <summary>The write/query sequence as comparable text, with expansions elided — the shared subject of
    /// both facts above.</summary>
    private static List<string> Describe(IReadOnlyList<CorpusStep> steps) =>
        [.. steps.Select(s => s switch
        {
            CorpusWrite w => $"W:{w.Write.Content}",
            CorpusQuery q => $"Q:{q.Text}|{string.Join(",", q.RelevantIds)}",
            CorpusExpand => null,
            _ => throw new InvalidOperationException($"unhandled step {s.GetType().Name}"),
        }).OfType<string>()];

    /// <summary>Every write step's own entry id, mapped to its index in <paramref name="steps"/>. There is
    /// exactly one write per id (critical-rare/topical/hot-ephemeral/noise are all written once), so the
    /// map is unambiguous.</summary>
    private static Dictionary<string, int> WriteIndexById(IReadOnlyList<CorpusStep> steps)
    {
        var map = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < steps.Count; i++)
            if (steps[i] is CorpusWrite w)
                map[ExtractId(w.Write.Content)] = i;
        return map;
    }

    /// <summary>How many real <see cref="CorpusWrite"/> steps sit strictly between a target's own write and
    /// the query at <paramref name="queryIndex"/> — this repository's definition of "age" for a query, and
    /// the same quantity <see cref="Lyntai.Memory.Interference.IMemoryAgePolicy"/>-driven interference measures
    /// when the corpus is actually replayed against a live engine.</summary>
    private static int InterposedWrites(IReadOnlyList<CorpusStep> steps, int writeIndex, int queryIndex) =>
        steps.Skip(writeIndex + 1).Take(queryIndex - writeIndex - 1).OfType<CorpusWrite>().Count();

    [Fact]
    public void No_reuse_query_occurs_at_age_zero()
    {
        // THE direct guard for the timing defect this fix closes. DsrRetrievability.Retrievability
        // short-circuits Age<=0 to a perfect 1.0 (as did the deleted HalfLifeRetrievability.Retrievability),
        // so a query fired in the same instant as its target's own write
        // is simultaneously the best textual match (its token is unique) AND unmissable by every policy —
        // it cannot ever register a miss, so it measures nothing.
        //
        // PROPERTY-BASED over a grid, not the sweep's six named shapes (fix round 4, F1): the six shapes in
        // bench/Lyntai.Benchmarks/MemoryPolicySweep.cs never exercise NoiseDensity=0 or
        // CandidateCount<=HotRounds, and the generator's flush path failed on exactly those two legal
        // shapes while every shape-pinned fact in this file stayed green — a guard whose coverage is pinned
        // to today's callers is not a guard. NoiseDensity spans 0 (a legal shape with no noise class at
        // all) through well above the sweep's own high-noise value; CandidateCount spans 0 through
        // HotRounds(5) — where hot-ephemeral, not topical, becomes the corpus's structurally last-written
        // class — through well above HotRounds.
        //
        // KNOWN, DELIBERATE EXCEPTION: the routine class's phase-B entries sit at age 0-3 relative to the
        // FINAL routine query, by design — that query's discriminating power is in the POLLUTION half (is
        // phase A excluded), not the miss half (is phase B found), so phase B is written fresh on purpose
        // (see MemoryCorpus.cs's ROUTINE, PART 2 comment). Invisible below only because every Shape here is
        // built positionally, leaving RoutineCount at its default of 0 — adding it to this grid will fail
        // this assertion for every routineB* id, and that failure is EXPECTED, not a regression.
        var noiseDensities = new[] { 0, 1, 2, 8, 40 };
        var candidateCounts = new[] { 0, 1, 3, 5, 10, 40 };
        var reuseRatios = new[] { 1, 10 };

        foreach (var noiseDensity in noiseDensities)
        foreach (var candidateCount in candidateCounts)
        foreach (var reuseRatio in reuseRatios)
        {
            var shape = new Shape(reuseRatio, noiseDensity, 6, candidateCount);
            var corpus = MemoryCorpus.Generate(shape, seed: 12345);
            var steps = corpus.Steps.ToList();
            var writeIndexById = WriteIndexById(steps);

            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i] is not CorpusQuery q || q.RelevantIds.Count == 0) continue;
                foreach (var id in q.RelevantIds)
                {
                    Assert.True(writeIndexById.TryGetValue(id, out var writeIndex),
                        $"[{shape}] query '{q.Text}' declares '{id}' relevant but no write for it exists");
                    var age = InterposedWrites(steps, writeIndex, i);
                    Assert.True(age >= 1,
                        $"[{shape}] '{id}' was queried ('{q.Text}') at age 0 — Age<=0 makes it unmissable "
                        + "by every retrievability policy, so this query would measure nothing");
                }
            }
        }
    }

    // The property grid shared by every age/N-band guard below — IDENTICAL to No_reuse_query_occurs_at_age_zero's
    // own grid, deliberately, so "property-based over the 60-shape grid" means the SAME 60 shapes everywhere in
    // this file rather than each guard quietly picking its own subset (the defect this whole retarget was asked
    // to close for good — TASKS.md Part 55 / the DSR-default falsification plan's Task 1 brief, "That defect has
    // recurred twice in this file already").
    private static readonly int[] GridNoiseDensities = [0, 1, 2, 8, 40];
    private static readonly int[] GridCandidateCounts = [0, 1, 3, 5, 10, 40];
    private static readonly int[] GridReuseRatios = [1, 10];

    private static IEnumerable<Shape> Grid(int criticalRarity = 6)
    {
        foreach (var noiseDensity in GridNoiseDensities)
        foreach (var candidateCount in GridCandidateCounts)
        foreach (var reuseRatio in GridReuseRatios)
            yield return new Shape(reuseRatio, noiseDensity, criticalRarity, candidateCount);
    }

    // DsrOptions.InitialStability defaults this to 20 (as did the deleted HalfLifeOptions.InitialStability,
    // at the time both existed) — see MemoryCorpus's own AssumedInitialStability, the same documented assumption
    // rather than a derived value, kept identical here so a query like "age/S" means the same ratio in the
    // corpus's own comments and in this test's assertions.
    private const int AssumedInitialStability = 20;

    [Fact]
    public void Topical_reuse_queries_reach_the_discriminating_bands_ceiling()
    {
        // REPLACES the single-shape "The_interference_range_reaches_a_meaningful_age" (2026-08-10, DSR-default
        // falsification plan Task 1 / TASKS.md Part 55): that version asserted a floor of 40 against ONE shape
        // ("high-noise") and let every other shape in the grid go unchecked — precisely the defect
        // No_reuse_query_occurs_at_age_zero was made property-based to stop recurring, recurring anyway one test
        // below it. PROPERTY-BASED here instead: every topical entry's own reuse queries are GUARANTEED (via
        // MemoryCorpus.TopUpTo, not merely scheduled) to reach TopicalReuseDelayWrites — the discriminating
        // band's own CEILING, 5 x AssumedInitialStability — on EVERY shape in the grid, not just a wide one.
        const int Ceiling = 5 * AssumedInitialStability; // = TopicalReuseDelayWrites, restated verbatim here so
                                                           // a change to that constant without a matching change
                                                           // here fails loudly rather than silently agreeing.

        foreach (var shape in Grid())
        {
            var corpus = MemoryCorpus.Generate(shape, seed: 12345);
            var steps = corpus.Steps.ToList();
            var writeIndexById = WriteIndexById(steps);

            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i] is not CorpusQuery q || q.RelevantIds.Count == 0) continue;
                if (!q.Text.Contains("topic", StringComparison.Ordinal)) continue;

                foreach (var id in q.RelevantIds)
                {
                    if (!writeIndexById.TryGetValue(id, out var writeIndex)) continue;
                    var age = InterposedWrites(steps, writeIndex, i);
                    Assert.True(age >= Ceiling,
                        $"[{shape}] topical query '{q.Text}' for '{id}' reached age {age} "
                        + $"(age/S={age / (double)AssumedInitialStability:F2}), below the discriminating band's "
                        + $"own ceiling of {Ceiling} (age/S={Ceiling / (double)AssumedInitialStability:F2})");
                }
            }
        }
    }

    [Fact]
    public void Hot_ephemeral_in_window_queries_reach_the_discriminating_bands_floor()
    {
        // REPLACES "Hot_ephemeral_in_window_queries_reach_a_discriminating_age" (2026-08-10, DSR-default
        // falsification plan Task 1 Step 3 / TASKS.md Part 55, closing that item). The OLD force-drain block in
        // MemoryCorpus.Generate dequeued and fired a round's in-window reuse batch UNCONDITIONALLY the instant it
        // reached the front of the queue, without ever checking DueWriteCount — bypassing HotReuseDelayWrites on
        // every shape but the widest one ("high-noise"), which is why the old guard could only pin a GlobalFloor
        // of 1 (age effectively unconstrained) plus a second, shape-specific floor of 9 for "high-noise" alone.
        // The force-drain now tops up with filler writes to the scheduled due count BEFORE firing (see
        // MemoryCorpus.TopUpTo), so every shape reaches AT LEAST HotReuseDelayWrites — the discriminating band's
        // own FLOOR, 1.5 x AssumedInitialStability — not just the one shape wide enough to get there naturally.
        const int Floor = 3 * AssumedInitialStability / 2; // = HotReuseDelayWrites, restated verbatim (1.5 x S)

        foreach (var shape in Grid())
        {
            var corpus = MemoryCorpus.Generate(shape, seed: 12345);
            var steps = corpus.Steps.ToList();
            var writeIndexById = WriteIndexById(steps);

            for (var i = 0; i < steps.Count; i++)
            {
                // "repeat" scopes this to IN-WINDOW queries specifically (see
                // A_hot_ephemeral_entrys_relevant_window_closes) — the stale queries (empty relevant set) are
                // exempt, since they declare nothing relevant and so carry no age for a curve to read at all.
                if (steps[i] is not CorpusQuery q || q.RelevantIds.Count == 0) continue;
                if (!q.Text.Contains("hot", StringComparison.Ordinal)) continue;
                if (!q.Text.Contains("repeat", StringComparison.Ordinal)) continue;

                foreach (var id in q.RelevantIds)
                {
                    if (!writeIndexById.TryGetValue(id, out var writeIndex)) continue;
                    var age = InterposedWrites(steps, writeIndex, i);
                    Assert.True(age >= Floor,
                        $"[{shape}] hot-ephemeral in-window query '{q.Text}' for '{id}' reached age {age} "
                        + $"(age/S={age / (double)AssumedInitialStability:F2}), below the discriminating band's "
                        + $"own floor of {Floor} (age/S={Floor / (double)AssumedInitialStability:F2}) — either "
                        + "the grid moved or the force-drain top-up regressed");
                }
            }
        }
    }

    [Fact]
    public void Critical_rare_queries_reach_the_discriminating_bands_midpoint()
    {
        // NEW (2026-08-10, DSR-default falsification plan Task 1 Step 1): critical-rare's own age is "the whole
        // rest of the corpus" by design (see MemoryCorpus's own remarks on criticalIds), which usually exceeds
        // this floor on its own once topical/hot-ephemeral's own — larger — delays above have run. This guards
        // the case neither of them covers: a shape with no topical entries at all (CandidateCount=0) and a thin
        // hot-ephemeral budget, which is exactly why MemoryCorpus.Generate tops up to this floor DIRECTLY rather
        // than depending on a neighbour class's own constant to get there incidentally.
        const int Midpoint = 3 * AssumedInitialStability; // = CriticalRareFloorWrites, restated verbatim (3 x S)

        foreach (var shape in Grid())
        {
            var corpus = MemoryCorpus.Generate(shape, seed: 12345);
            var steps = corpus.Steps.ToList();
            var writeIndexById = WriteIndexById(steps);

            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i] is not CorpusQuery q || q.RelevantIds.Count == 0) continue;
                if (!q.Text.Contains("critical", StringComparison.Ordinal)) continue;

                foreach (var id in q.RelevantIds)
                {
                    if (!writeIndexById.TryGetValue(id, out var writeIndex)) continue;
                    var age = InterposedWrites(steps, writeIndex, i);
                    Assert.True(age >= Midpoint,
                        $"[{shape}] critical-rare query '{q.Text}' for '{id}' reached age {age} "
                        + $"(age/S={age / (double)AssumedInitialStability:F2}), below the discriminating band's "
                        + $"own midpoint of {Midpoint} (age/S={Midpoint / (double)AssumedInitialStability:F2})");
                }
            }
        }
    }

    [Fact]
    public void Critical_rare_clears_its_independent_target_floor_at_its_rarest_named_setting()
    {
        // NEW (2026-08-10, DSR-default falsification plan Task 1 Step 2 / TASKS.md Part 55): critical-rare is
        // the DECIDING class for the curve question, and it used to carry only 2-4 independent targets per cell
        // (CriticalBudget=12), so a single entry flipping moved a cell's MissRate by 0.25-0.5. CriticalRarity=12
        // is this corpus's own rarest NAMED setting (bench/Lyntai.Benchmarks/MemoryPolicySweep.cs's
        // "rare-critical" shape) — this pins the floor there, the hardest case, on the same Shape.Default base
        // every other single-shape fact in this file uses.
        const int RarestNamedCriticalRarity = 12;
        const int IndependentTargetFloor = 20;

        var corpus = MemoryCorpus.Generate(Shape.Default with { CriticalRarity = RarestNamedCriticalRarity }, seed: 12345);
        var criticalCount = corpus.Steps.OfType<CorpusWrite>()
            .Count(w => w.Write.Content.Contains("item critical", StringComparison.Ordinal));

        Assert.True(criticalCount >= IndependentTargetFloor,
            $"critical-rare carries only {criticalCount} independent targets at its rarest named setting "
            + $"(CriticalRarity={RarestNamedCriticalRarity}) — below the {IndependentTargetFloor}+ floor the "
            + "curve question needs to keep a single entry flipping from moving a cell by more than a few percent");
    }

    /// <summary>Opt-in, and the default shape is untouched — same guarantee, same reasoning as
    /// <see cref="ExpandRatio_defaults_to_zero_and_changes_nothing"/>.</summary>
    [Fact]
    public void RoutineCount_defaults_to_zero_and_changes_nothing()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default, seed: 4242);

        Assert.Equal(0, CorpusShape.Default.RoutineCount);
        Assert.DoesNotContain(corpus.Steps.OfType<CorpusWrite>(),
            w => ExtractId(w.Write.Content).StartsWith("routine", StringComparison.Ordinal));

        var explicitly = MemoryCorpus.Generate(
            new CorpusShape(ReuseRatio: 4, NoiseDensity: 8, CriticalRarity: 6, CandidateCount: 10), seed: 4242);
        Assert.Equal(Describe(explicitly.Steps), Describe(corpus.Steps));
    }

    /// <summary>A count that cannot honour "phase A is the larger regime" is refused outright rather than
    /// silently generating a degenerate corpus — 1 inverts to all-B, 2 can only tie.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void RoutineCount_below_three_is_refused(int routineCount)
    {
        Assert.Throws<ArgumentException>(
            () => MemoryCorpus.Generate(CorpusShape.Default with { RoutineCount = routineCount }, seed: 4242));
    }

    /// <summary>PROPERTY-BASED over a range of counts, not one hand-picked value — this file's own class doc
    /// records this exact defect class recurring twice already from a hand-picked shape. The golden shape
    /// used to be RoutineCount=9, an exact multiple of 3, where the OLD <c>count * 2 / 3</c> formula happened
    /// to agree with the fixed <c>Math.Max(1, count / 3)</c> one; RoutineCount=4 did not (2/2, a tie).</summary>
    [Fact]
    public void Phase_A_is_the_larger_regime_for_every_legal_RoutineCount()
    {
        for (var routineCount = 3; routineCount <= 30; routineCount++)
        {
            var corpus = MemoryCorpus.Generate(CorpusShape.Default with { RoutineCount = routineCount }, seed: 4242);
            var ids = corpus.Steps.OfType<CorpusWrite>().Select(w => ExtractId(w.Write.Content)).ToList();

            var phaseACount = ids.Count(id => id.StartsWith("routineA", StringComparison.Ordinal));
            var phaseBCount = ids.Count(id => id.StartsWith("routineB", StringComparison.Ordinal));

            Assert.Equal(routineCount, phaseACount + phaseBCount);
            Assert.True(phaseACount > phaseBCount,
                $"RoutineCount={routineCount}: phase A ({phaseACount}) is not larger than phase B "
                + $"({phaseBCount}) — a generalisation built on support count alone would pass by accident");
        }
    }

    /// <summary>The property the class exists for, stated directly rather than only through the split count
    /// above: the FINAL routine query's own relevant set is phase-B ids ONLY and names not one phase-A id —
    /// the ground truth a support-count-only generalisation gets confidently wrong. The FIRST routine query
    /// is the mirror image, naming phase-A ids only (there is no phase B yet to name).</summary>
    [Fact]
    public void The_final_routine_query_names_phase_B_only_and_never_phase_A()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default with { RoutineCount = 12 }, seed: 4242);
        var lex = CorpusLexicon.For(CorpusLanguage.English);

        var routineQueries = corpus.Steps.OfType<CorpusQuery>()
            .Where(q => q.Text == lex.RoutineQuery())
            .ToList();
        Assert.Equal(2, routineQueries.Count);
        var (first, final) = (routineQueries[0], routineQueries[1]);

        Assert.Equal(8, first.RelevantIds.Count);
        Assert.All(first.RelevantIds, id => Assert.StartsWith("routineA", id, StringComparison.Ordinal));

        Assert.Equal(4, final.RelevantIds.Count);
        Assert.All(final.RelevantIds, id => Assert.StartsWith("routineB", id, StringComparison.Ordinal));
    }

    /// <summary>Phase A's own query (the first one) must clear the discriminating band's FLOOR
    /// (<c>HotReuseDelayWrites</c>) for every one of its ids, not merely be nonzero — mirrors
    /// <see cref="Hot_ephemeral_in_window_queries_reach_the_discriminating_bands_floor"/>. Guards the
    /// <c>TopUpTo</c> call <c>MemoryCorpus.Generate</c> makes right after phase A's writes.</summary>
    [Fact]
    public void Routine_phase_As_own_query_clears_the_discriminating_bands_floor()
    {
        const int Floor = 3 * AssumedInitialStability / 2; // = HotReuseDelayWrites, restated verbatim (1.5 x S)
        var lex = CorpusLexicon.For(CorpusLanguage.English);

        foreach (var baseShape in Grid())
        {
            var shape = baseShape with { RoutineCount = 12 };
            var corpus = MemoryCorpus.Generate(shape, seed: 12345);
            var steps = corpus.Steps.ToList();
            var writeIndexById = WriteIndexById(steps);

            var firstQueryIndex = steps.FindIndex(s => s is CorpusQuery q && q.Text == lex.RoutineQuery());
            Assert.True(firstQueryIndex >= 0, $"[{shape}] no routine query found");
            var firstQuery = (CorpusQuery)steps[firstQueryIndex];

            foreach (var id in firstQuery.RelevantIds)
            {
                var age = InterposedWrites(steps, writeIndexById[id], firstQueryIndex);
                Assert.True(age >= Floor,
                    $"[{shape}] phase-A entry '{id}' reached age {age} at its own routine query, below the "
                    + $"discriminating band's own floor of {Floor} (age/S={Floor / (double)AssumedInitialStability:F2})");
            }
        }
    }

    /// <summary>The whole premise this class exists to test — a generalisation built on support count alone,
    /// ignoring recency, is confidently wrong — is untestable unless phase A has genuinely AGED relative to
    /// the final query, AND stays inside the region where DSR's own curve still discriminates rather than
    /// having collapsed to its near-zero floor. Both bounds are PROPERTY-BASED over <see cref="Grid"/>: the
    /// floor mirrors <see cref="Critical_rare_queries_reach_the_discriminating_bands_midpoint"/>, the ceiling
    /// mirrors <see cref="Dsr_is_not_floored_at_the_grids_largest_reached_age"/>'s own threshold. Without the
    /// ceiling, nothing here would fail if some future change pushed phase A far enough out that DSR's curve
    /// floors — the exact over-correction a placement fix like this one has to be checked against.</summary>
    [Fact]
    public void Routine_phase_A_reaches_the_discriminating_bands_midpoint_at_the_final_query()
    {
        const int Midpoint = 3 * AssumedInitialStability; // = CriticalRareFloorWrites, restated verbatim (3 x S)
        var lex = CorpusLexicon.For(CorpusLanguage.English);
        // Same threshold and same curve as Dsr_is_not_floored_at_the_grids_largest_reached_age, so "aged
        // enough to discriminate" means the same thing in both places.
        var dsr = new DsrRetrievability();
        double DsrR(double age) => dsr.Retrievability(new MemoryDecayState(Age: age, RecallCount: 0, Stability: 0));

        foreach (var baseShape in Grid())
        {
            var shape = baseShape with { RoutineCount = 12 };
            var corpus = MemoryCorpus.Generate(shape, seed: 12345);
            var steps = corpus.Steps.ToList();
            var writeIndexById = WriteIndexById(steps);

            var finalQueryIndex = steps.FindLastIndex(s => s is CorpusQuery q && q.Text == lex.RoutineQuery());
            Assert.True(finalQueryIndex >= 0, $"[{shape}] no routine query found");

            var phaseAIds = steps.OfType<CorpusWrite>()
                .Select(w => ExtractId(w.Write.Content))
                .Where(id => id.StartsWith("routineA", StringComparison.Ordinal));

            foreach (var id in phaseAIds)
            {
                var age = InterposedWrites(steps, writeIndexById[id], finalQueryIndex);
                Assert.True(age >= Midpoint,
                    $"[{shape}] phase-A entry '{id}' reached age {age} (age/S={age / (double)AssumedInitialStability:F2}) "
                    + $"at the final routine query, below the discriminating band's own midpoint of {Midpoint} "
                    + $"(age/S={Midpoint / (double)AssumedInitialStability:F2}) — phase A cannot be scored as "
                    + "pollution over recency if it never aged relative to phase B");

                var r = DsrR(age);
                Assert.True(r >= 0.05,
                    $"[{shape}] phase-A entry '{id}' reached age {age} where DSR's own retrievability is "
                    + $"{r:F3} — collapsed into the near-zero floor, which makes phase A undifferentiable "
                    + "pollution rather than a class an engine's ranking can actually be tested against");
            }
        }
    }

    [Fact]
    public void Reuse_repeats_never_fire_back_to_back_a_real_write_always_interposes()
    {
        // NEW (2026-08-10, DSR-default falsification plan Task 1 Step 2 / TASKS.md Part 55): before this fix, a
        // reuse batch's `reuse` repeats fired with NOTHING interposed between them — correlated draws of the
        // same retrieval decision, not independent ones, so a printed N of (say) 100 at ReuseRatio=10 carried
        // the granularity of 10 independent targets, not 100. PROPERTY-BASED over the same grid, at
        // ReuseRatio=10 specifically so there are repeats to check at all (ReuseRatio=1 has none, vacuously).
        foreach (var noiseDensity in GridNoiseDensities)
        foreach (var candidateCount in GridCandidateCounts)
        {
            var shape = new Shape(ReuseRatio: 10, noiseDensity, CriticalRarity: 6, candidateCount);
            var corpus = MemoryCorpus.Generate(shape, seed: 12345);
            var steps = corpus.Steps.ToList();

            // Every repeat query's text ends "repeat{k}" (topical: "item {id} repeat{k}"; hot-ephemeral:
            // "item {id} focus repeat{k}") — group consecutive-in-timeline repeat queries for the SAME relevant
            // id and check a real write sits between every adjacent pair.
            var repeatQueryIndexesById = new Dictionary<string, List<int>>(StringComparer.Ordinal);
            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i] is not CorpusQuery q || q.RelevantIds.Count != 1) continue;
                if (!q.Text.Contains("repeat", StringComparison.Ordinal)) continue;
                var id = q.RelevantIds[0];
                if (!repeatQueryIndexesById.TryGetValue(id, out var list)) repeatQueryIndexesById[id] = list = [];
                list.Add(i);
            }

            foreach (var (id, indexes) in repeatQueryIndexesById)
            for (var k = 1; k < indexes.Count; k++)
            {
                var writesBetween = steps.Skip(indexes[k - 1] + 1).Take(indexes[k] - indexes[k - 1] - 1)
                    .OfType<CorpusWrite>().Count();
                Assert.True(writesBetween >= 1,
                    $"[{shape}] '{id}''s reuse repeats {k - 1} and {k} fired with no write interposed between "
                    + "them — a correlated draw of the same retrieval decision, not an independent one");
            }
        }
    }

    /// <summary><b>Filler must not be able to COMPETE, which is a strictly stronger property than "is never
    /// declared relevant" — the fact below — and the corpus failed it for its whole life.</b> Every filler
    /// write used to begin <c>"item filler{n} …"</c>, sharing the token <c>item</c> with every real entry AND
    /// with almost every query; <see cref="Lyntai.Storage.FtsQuery.Build"/> OR-joins a query's tokens, so a
    /// filler written moments ago (retrievability ≈ 1) was a legitimate candidate for a query it has nothing
    /// to do with, and could out-score an already-decayed but genuinely relevant target. Measured
    /// consequence: a handful of early <c>topic*</c>/<c>hot*</c> entries were never recalled for ANY of their
    /// own relevant queries, so <c>Reinforce</c> was never even CALLED for them.
    /// <para>That is a defect in the MEASURING INSTRUMENT, not in any policy — filler exists purely to
    /// interpose writes and advance interference (<see cref="MemoryCorpus"/>'s <c>TopUpTo</c>), and a
    /// padding class that quietly enters the ranked competition it was added to stand outside of makes every
    /// number measured over it partly a measurement of the padding.</para>
    /// <para>Checked as SUBSTRING containment rather than token equality on purpose: the SQL backends index
    /// content with an FTS5 <b>trigram</b> tokenizer, so a query term matches any document containing it as a
    /// substring — token-boundary equality would pass while the store still matched. The 3-character floor is
    /// <see cref="Lyntai.Storage.FtsQuery.Build"/>'s own rule (shorter tokens are dropped, a trigram index
    /// cannot match them), so a shared <c>"to"</c> or <c>"is"</c> is genuinely harmless and correctly
    /// ignored.</para></summary>
    [Fact]
    public void No_filler_entry_can_match_any_query_in_the_corpus()
        => NoFillerCanMatchAnyQuery(CorpusLanguage.English);

    /// <summary><b>The same guarantee in every non-English language, and it is HARDER to hold there.</b> Under
    /// trigram matching almost any two CJK texts share trigrams, so padding that competes is far easier to
    /// write by accident than in English — and this corpus already paid once for padding that competed (see
    /// <see cref="MemoryCorpus"/>'s note on filler that began "item filler{n}"). A variant without this check
    /// would be measuring its own scaffolding and reporting it as recall quality.
    /// <para><b>Japanese is the sharpest case</b>: kana words are frequently two characters and hiragana's
    /// inventory is small, so three common kana can appear in unrelated sentences by chance far more readily
    /// than three Han characters can.</para></summary>
    [Theory]
    [MemberData(nameof(NonEnglishLanguages))]
    public void No_filler_entry_can_match_any_query_in_a_non_english_corpus(CorpusLanguage language)
        => NoFillerCanMatchAnyQuery(language);

    private static void NoFillerCanMatchAnyQuery(CorpusLanguage language)
    {
        foreach (var baseShape in Grid())
        {
            var shape = baseShape with { Language = language };
            var corpus = MemoryCorpus.Generate(shape, seed: 11);

            var fillers = corpus.Steps.OfType<CorpusWrite>()
                .Select(w => w.Write.Content)
                .Where(c => ExtractId(c).StartsWith("filler", StringComparison.Ordinal))
                .ToList();
            Assert.NotEmpty(fillers); // every shape tops up at least once, so this is never vacuous

            // SearchTerms, not a space split: in Chinese the store matches on character trigrams, so a
            // word-split check would compare terms the store never uses and pass while filler still matched.
            var queryTerms = corpus.Steps.OfType<CorpusQuery>()
                .SelectMany(q => Lyntai.Storage.SearchTerms.Extract(q.Text))
                .ToHashSet(StringComparer.Ordinal);
            Assert.NotEmpty(queryTerms);

            foreach (var content in fillers)
            foreach (var term in queryTerms)
                Assert.False(content.Contains(term, StringComparison.Ordinal),
                    $"[{shape}] a filler entry can be matched by the query term '{term}' — filler is " +
                    "padding, and padding that competes for ranked slots corrupts every measurement taken " +
                    $"over this corpus. Offending content: \"{content}\"");
        }
    }

    [Fact]
    public void Filler_entries_are_never_declared_relevant_to_any_query()
    {
        // NEW (2026-08-10): fillers are the write class MemoryCorpus.Generate's TopUpTo introduces purely to
        // interpose age — the same never-relevant guarantee Noise_entries_are_never_declared_relevant_to_any_query
        // already pins for noise, extended to the corpus's other inert write class.
        var corpus = MemoryCorpus.Generate(Shape.Default with { CandidateCount = 0, NoiseDensity = 0 }, seed: 7);

        var fillerWrites = corpus.Steps.OfType<CorpusWrite>()
            .Where(w => w.Write.Content.Contains("padding filler", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(fillerWrites); // this shape's own thin budget forces top-up to actually fire

        var everDeclaredRelevant = corpus.Steps.OfType<CorpusQuery>()
            .SelectMany(q => q.RelevantIds)
            .Any(id => id.StartsWith("filler", StringComparison.Ordinal));

        Assert.False(everDeclaredRelevant,
            "a filler entry must never displace one of the declared classes from a relevant set");
    }

    [Fact]
    public void Dsr_is_not_floored_at_the_grids_largest_reached_age()
    {
        // Retargeted (2026-08-10, fsrs-properly plan Task 1) from a two-curve divergence check: this corpus
        // was tuned into the age/S band where DSR and the exponential curve it used to ship beside
        // (HalfLifeRetrievability, deleted in 3.0 — docs/DECISIONS.md D49) diverge, specifically so a sweep
        // comparing the two could tell them apart. That comparison is now moot — there is only one shipped
        // curve — but the property this fact actually needs from the CORPUS survives the curve's deletion:
        // the ages it drives entries to must not be so extreme that DSR's own power-law tail collapses to the
        // same near-zero floor a much steeper curve would reach. A corpus that floors its own curve at its
        // largest ages is measuring "everything is unrecallable" rather than the forgetting model, whichever
        // curve is under test.
        var dsr = new DsrRetrievability();

        // Stability: 0 lets the policy substitute its OWN InitialStability (DsrOptions defaults it to 20 —
        // MemoryDecayState's own contract), rather than this test hardcoding a number that could silently
        // drift from the policy's actual default.
        double DsrR(double age) => dsr.Retrievability(new MemoryDecayState(Age: age, RecallCount: 0, Stability: 0));

        var minDsrAtMaxAge = double.MaxValue;

        foreach (var shape in Grid())
        {
            var corpus = MemoryCorpus.Generate(shape, seed: 12345);
            var steps = corpus.Steps.ToList();
            var writeIndexById = WriteIndexById(steps);
            var maxAgeThisShape = 0;

            for (var i = 0; i < steps.Count; i++)
            {
                if (steps[i] is not CorpusQuery q || q.RelevantIds.Count == 0) continue;
                var isTracked = q.Text.Contains("topic", StringComparison.Ordinal)
                    || (q.Text.Contains("hot", StringComparison.Ordinal) && q.Text.Contains("repeat", StringComparison.Ordinal))
                    || q.Text.Contains("critical", StringComparison.Ordinal);
                if (!isTracked) continue;

                foreach (var id in q.RelevantIds)
                {
                    if (!writeIndexById.TryGetValue(id, out var writeIndex)) continue;
                    var age = InterposedWrites(steps, writeIndex, i);
                    if (age <= 0) continue;

                    maxAgeThisShape = Math.Max(maxAgeThisShape, age);
                }
            }

            if (maxAgeThisShape > 0)
                minDsrAtMaxAge = Math.Min(minDsrAtMaxAge, DsrR(maxAgeThisShape));
        }

        // MEASURED (seed 12345, this exact grid): minDsrAtMaxAge ≈ 0.106 (critical-rare's widest shape,
        // age≈591) — the threshold below sits comfortably under that, not hugging it, so a small future
        // constant change does not make this brittle.
        Assert.True(minDsrAtMaxAge >= 0.05,
            $"Dsr's own retrievability fell to {minDsrAtMaxAge:F3} at some class's largest reached age — "
            + "floored rather than remaining the class's discriminating signal");
    }

    [Fact]
    public void The_same_seed_produces_a_byte_identical_corpus()
    {
        // determinism is not a nicety here: a sweep whose corpus moves between runs cannot be compared
        // against itself, and every policy difference it reports would be noise.
        var a = MemoryCorpus.Generate(Shape.Default, seed: 12345);
        var b = MemoryCorpus.Generate(Shape.Default, seed: 12345);

        Assert.Equal(
            a.Steps.OfType<CorpusWrite>().Select(w => w.Write.Content),
            b.Steps.OfType<CorpusWrite>().Select(w => w.Write.Content));
        Assert.Equal(
            a.Steps.OfType<CorpusQuery>().Select(q => q.Text),
            b.Steps.OfType<CorpusQuery>().Select(q => q.Text));
    }

    [Fact]
    public void A_different_seed_produces_a_different_corpus()
    {
        // guards the opposite failure: a generator that ignores its seed is deterministic AND useless,
        // and the test above would pass just as happily.
        var a = MemoryCorpus.Generate(Shape.Default, seed: 1);
        var b = MemoryCorpus.Generate(Shape.Default, seed: 2);

        Assert.NotEqual(
            a.Steps.OfType<CorpusWrite>().Select(w => w.Write.Content),
            b.Steps.OfType<CorpusWrite>().Select(w => w.Write.Content));
    }

    [Fact]
    public void Noise_entries_are_never_declared_relevant_to_any_query()
    {
        var corpus = MemoryCorpus.Generate(Shape.Default, seed: 7);

        var noiseWrites = corpus.Steps.OfType<CorpusWrite>()
            .Where(w => w.Write.Content.Contains("item noise", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(noiseWrites);

        var everDeclaredRelevant = corpus.Steps.OfType<CorpusQuery>()
            .SelectMany(q => q.RelevantIds)
            .Any(id => id.StartsWith("noise", StringComparison.Ordinal));

        Assert.False(everDeclaredRelevant,
            "a noise entry must never displace one of the declared classes from a relevant set");
    }

    [Fact]
    public void A_critical_rare_entry_is_written_exactly_once_and_is_relevant_to_at_least_one_query()
    {
        var corpus = MemoryCorpus.Generate(Shape.Default, seed: 7);

        var writes = corpus.Steps.OfType<CorpusWrite>().Where(w => Mentions(w.Write.Content, "critical0")).ToList();
        Assert.Single(writes);

        var relevantQueries = corpus.Steps.OfType<CorpusQuery>().Count(q => q.RelevantIds.Contains("critical0"));
        Assert.True(relevantQueries >= 1, "a critical-rare entry that no query ever asks for cannot be "
            + "proven recalled or missed");
    }

    [Fact]
    public void A_hot_ephemeral_entrys_relevant_window_closes()
    {
        var corpus = MemoryCorpus.Generate(Shape.Default, seed: 7);

        // "repeat" scopes this to hot0's own IN-WINDOW queries, not the later "stale" re-ask (below) which
        // also mentions "hot0" in its text but deliberately does NOT declare it relevant.
        var earlyQueries = corpus.Steps.OfType<CorpusQuery>()
            .Where(q => Mentions(q.Text, "hot0") && q.Text.Contains("repeat", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(earlyQueries);
        Assert.All(earlyQueries, q => Assert.Contains("hot0", q.RelevantIds));

        // a later round's own queries exist, and NONE of them still call the earlier round's id relevant —
        // that is the window closing, asserted on the declared data rather than on any live engine.
        var laterQueries = corpus.Steps.OfType<CorpusQuery>().Where(q => Mentions(q.Text, "hot1")).ToList();
        Assert.NotEmpty(laterQueries);
        Assert.All(laterQueries, q => Assert.DoesNotContain("hot0", q.RelevantIds));
    }

    [Fact]
    public void The_timeline_genuinely_interleaves_writes_and_queries()
    {
        // Guards the defect this whole redesign exists to fix: a generator that silently collapsed back
        // into write-everything-then-query-everything would still satisfy every OTHER fact in this file
        // (each query's declared relevant set would just be evaluated against the final state) while making
        // the hot-ephemeral window and the critical-rare "late lookup" both meaningless — the corpus would
        // have no time axis at all. So this is checked directly, on the timeline's own shape.
        var corpus = MemoryCorpus.Generate(Shape.Default, seed: 7);
        var steps = corpus.Steps.ToList();

        var firstQueryIndex = steps.FindIndex(s => s is CorpusQuery);
        var lastWriteIndex = steps.FindLastIndex(s => s is CorpusWrite);

        Assert.True(firstQueryIndex >= 0, "the timeline must contain at least one query step");
        Assert.True(lastWriteIndex >= 0, "the timeline must contain at least one write step");
        Assert.True(firstQueryIndex < lastWriteIndex,
            "a query step must be followed by a LATER write step, or this is really write-then-query "
            + "carried out in two phases with extra ceremony");
    }

    [Fact]
    public void A_hot_ephemeral_entrys_window_closes_only_after_real_interference()
    {
        // The stronger version of the fact above: the SAME conceptual lookup (same entry, same query
        // shape) is answered differently purely because of where in the timeline it is asked — and that
        // difference is backed by actual intervening writes, not just two labels ("early"/"late") with
        // nothing behind them.
        var corpus = MemoryCorpus.Generate(Shape.Default, seed: 7);
        var steps = corpus.Steps.ToList();

        var inWindowIndex = steps.FindIndex(s =>
            s is CorpusQuery q && Mentions(q.Text, "hot0") && q.RelevantIds.Contains("hot0"));
        Assert.True(inWindowIndex >= 0, "hot0 must have at least one in-window query that declares it relevant");

        var staleIndex = steps.FindIndex(s =>
            s is CorpusQuery q && q.Text.Contains("hot0 focus stale", StringComparison.Ordinal));
        Assert.True(staleIndex >= 0, "hot0 must be looked up again later, outside its window");
        Assert.True(staleIndex > inWindowIndex, "the stale lookup must come strictly AFTER the in-window one");

        var staleQuery = (CorpusQuery)steps[staleIndex];
        Assert.DoesNotContain("hot0", staleQuery.RelevantIds);

        var interferingWrites = steps
            .Skip(inWindowIndex + 1)
            .Take(staleIndex - inWindowIndex - 1)
            .OfType<CorpusWrite>()
            .Count();
        Assert.True(interferingWrites > 0,
            "the window must close because real writes happened in between, not because of list position alone");
    }

    [Fact]
    public void A_critical_rare_entrys_relevant_query_sits_after_substantial_interference()
    {
        var corpus = MemoryCorpus.Generate(Shape.Default, seed: 7);
        var steps = corpus.Steps.ToList();

        var writeIndex = steps.FindIndex(s => s is CorpusWrite w && Mentions(w.Write.Content, "critical0"));
        Assert.True(writeIndex >= 0);

        var queryIndex = steps.FindIndex(s => s is CorpusQuery q && q.RelevantIds.Contains("critical0"));
        Assert.True(queryIndex > writeIndex, "the ground-truth query must come after the write");

        var interferingWrites = steps
            .Skip(writeIndex + 1)
            .Take(queryIndex - writeIndex - 1)
            .OfType<CorpusWrite>()
            .Count();

        // the threshold is CandidateCount itself — every topical write alone must fit in the gap, so this
        // can never degrade into a trivially fresh lookup no matter how the middle section is arranged
        Assert.True(interferingWrites >= Shape.Default.CandidateCount,
            $"critical0's ground-truth query should sit behind substantial interference, only "
            + $"{interferingWrites} write(s) came in between");
    }

    [Fact]
    public void A_topical_entry_is_relevant_only_to_its_own_queries()
    {
        var corpus = MemoryCorpus.Generate(Shape.Default, seed: 7);

        var ownQueries = corpus.Steps.OfType<CorpusQuery>().Where(q => q.RelevantIds.Contains("topic0")).ToList();
        Assert.NotEmpty(ownQueries);
        Assert.All(ownQueries, q => Assert.Contains("topic0", q.Text));

        var otherTopicQueries = corpus.Steps.OfType<CorpusQuery>().Where(q => Mentions(q.Text, "topic1"));
        Assert.All(otherTopicQueries, q => Assert.DoesNotContain("topic0", q.RelevantIds));
    }

    [Fact]
    public void Raising_NoiseDensity_produces_more_noise_entries()
    {
        var low = MemoryCorpus.Generate(Shape.Default with { NoiseDensity = 2 }, seed: 7);
        var high = MemoryCorpus.Generate(Shape.Default with { NoiseDensity = 20 }, seed: 7);

        var lowCount = low.Steps.OfType<CorpusWrite>()
            .Count(w => w.Write.Content.Contains("item noise", StringComparison.Ordinal));
        var highCount = high.Steps.OfType<CorpusWrite>()
            .Count(w => w.Write.Content.Contains("item noise", StringComparison.Ordinal));

        Assert.Equal(2, lowCount);
        Assert.Equal(20, highCount);
    }

    [Fact]
    public void Raising_CriticalRarity_produces_fewer_critical_rare_entries()
    {
        var common = MemoryCorpus.Generate(Shape.Default with { CriticalRarity = 1 }, seed: 7);
        var rare = MemoryCorpus.Generate(Shape.Default with { CriticalRarity = 12 }, seed: 7);

        var commonCount = common.Steps.OfType<CorpusWrite>()
            .Count(w => w.Write.Content.Contains("item critical", StringComparison.Ordinal));
        var rareCount = rare.Steps.OfType<CorpusWrite>()
            .Count(w => w.Write.Content.Contains("item critical", StringComparison.Ordinal));

        Assert.True(rareCount < commonCount,
            $"raising CriticalRarity should make the critical-rare class scarcer, not {rareCount} vs {commonCount}");
    }

    [Fact]
    public void Raising_CandidateCount_produces_more_topical_entries()
    {
        var small = MemoryCorpus.Generate(Shape.Default with { CandidateCount = 3 }, seed: 7);
        var large = MemoryCorpus.Generate(Shape.Default with { CandidateCount = 30 }, seed: 7);

        var smallCount = small.Steps.OfType<CorpusWrite>()
            .Count(w => w.Write.Content.Contains("item topic", StringComparison.Ordinal));
        var largeCount = large.Steps.OfType<CorpusWrite>()
            .Count(w => w.Write.Content.Contains("item topic", StringComparison.Ordinal));

        Assert.Equal(3, smallCount);
        Assert.Equal(30, largeCount);
    }

    [Fact]
    public void Raising_ReuseRatio_produces_more_queries()
    {
        var low = MemoryCorpus.Generate(Shape.Default with { ReuseRatio = 1 }, seed: 7);
        var high = MemoryCorpus.Generate(Shape.Default with { ReuseRatio = 10 }, seed: 7);

        var lowCount = low.Steps.OfType<CorpusQuery>().Count();
        var highCount = high.Steps.OfType<CorpusQuery>().Count();
        Assert.True(highCount > lowCount,
            $"raising ReuseRatio should generate more recall queries, not {highCount} vs {lowCount}");
    }

    [Fact]
    public void Raising_ReuseRatio_increases_query_steps_that_rehit_already_written_entries()
    {
        // The parameter's effect expressed against the TIMELINE rather than a flat total: a query step
        // that "re-hits" an entry is one whose relevant set is non-empty (topical/hot-ephemeral entries
        // reused while current). A flat total (the fact above) could in principle be satisfied by growth
        // elsewhere; this pins it to the mechanism ReuseRatio actually names.
        var low = MemoryCorpus.Generate(Shape.Default with { ReuseRatio = 1 }, seed: 7);
        var high = MemoryCorpus.Generate(Shape.Default with { ReuseRatio = 10 }, seed: 7);

        var lowRehits = low.Steps.OfType<CorpusQuery>().Count(q => q.RelevantIds.Count > 0);
        var highRehits = high.Steps.OfType<CorpusQuery>().Count(q => q.RelevantIds.Count > 0);

        Assert.True(highRehits > lowRehits,
            $"raising ReuseRatio should increase re-hit query steps in the timeline, not {highRehits} vs {lowRehits}");
    }

    /// <summary><b>A paraphrase cue must share NO index term with the statement it targets — that property
    /// IS the class.</b>
    ///
    /// <para>If any pair overlaps, the lexical path can answer it and the class silently degrades into an
    /// ordinary keyword recall, reporting semantic retrieval as working when nothing semantic happened.
    /// This is the same failure the authoritative probe guards against, and it is checked against the
    /// library's OWN tokenizer rather than a whitespace split, because a Chinese pair can share a trigram
    /// while sharing no word.</para>
    ///
    /// <para>Also asserts the cues are mutually distinguishable: a cue that matched two statements equally
    /// would make ground truth ambiguous and the miss rate meaningless.</para></summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void A_paraphrase_cue_shares_no_index_term_with_the_statement_it_targets(CorpusLanguage language)
    {
        var lex = CorpusLexicon.For(language);
        Assert.NotEmpty(lex.ParaphrasePairs);

        var statements = lex.ParaphrasePairs
            .Select(p => Lyntai.Storage.SearchTerms.Extract(p.Statement).ToHashSet(StringComparer.Ordinal))
            .ToList();

        for (var i = 0; i < lex.ParaphrasePairs.Count; i++)
        {
            var cue = Lyntai.Storage.SearchTerms.Extract(lex.ParaphrasePairs[i].Cue)
                .ToHashSet(StringComparer.Ordinal);
            Assert.NotEmpty(cue);

            var shared = cue.Intersect(statements[i], StringComparer.Ordinal).ToList();
            Assert.True(shared.Count == 0,
                $"[{language}] pair {i} shares {string.Join(", ", shared)} — the lexical path can answer " +
                "this cue, so it measures keyword recall rather than semantic retrieval");
        }
    }

    // ---- diverse noise: the axis that makes the salience inversion reachable at all ----

    /// <summary><b>The axis is byte-identical when unset, in every language.</b> Same rule every other
    /// corpus axis carries: a new dial that perturbs the default corpus would silently invalidate every
    /// number this repository has published. The <c>Templated</c> path draws from the RNG exactly as it did
    /// before the branch existed, which is the part that actually has to be checked — an identical template
    /// with a shifted draw sequence still changes the corpus downstream.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Diverse_noise_is_byte_identical_when_unset(CorpusLanguage language)
    {
        var shape = Shape.Default with { Language = language };
        var withoutAxis = MemoryCorpus.Generate(shape, seed: 11);
        var explicitlyTemplated =
            MemoryCorpus.Generate(shape with { NoiseKind = CorpusNoiseKind.Templated }, seed: 11);

        Assert.Equal(Render(withoutAxis), Render(explicitlyTemplated));
    }

    /// <summary><b>Templated noise really is near-identical, which is the DEFECT this axis exists to
    /// route around.</b> Stated as a positive fact rather than left implicit: if the shipped noise were
    /// already diverse, the whole axis would be unnecessary and the Part 53 hypothesis would already have
    /// been under test. Two templated noise entries share almost every term; two diverse ones share
    /// almost none.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Templated_noise_entries_are_near_identical_while_diverse_ones_are_not(CorpusLanguage language)
    {
        var shape = Shape.Default with { Language = language, NoiseDensity = 12 };

        var templated = NoiseTexts(MemoryCorpus.Generate(shape, seed: 5));
        var diverse = NoiseTexts(
            MemoryCorpus.Generate(shape with { NoiseKind = CorpusNoiseKind.Diverse }, seed: 5));

        Assert.True(templated.Count >= 2 && diverse.Count >= 2, "need two noise entries to compare");

        var templatedOverlap = MeanJaccard(templated);
        var diverseOverlap = MeanJaccard(diverse);

        Assert.True(templatedOverlap > 0.5,
            $"[{language}] templated noise should share most of its terms; mean Jaccard {templatedOverlap:F3}");
        Assert.True(diverseOverlap < 0.2,
            $"[{language}] diverse noise should share almost none; mean Jaccard {diverseOverlap:F3}");
    }

    /// <summary>Diverse noise stays NOISE — it is never a right answer to any query. The axis changes how
    /// junk is WORDED, never whether it is junk, and a diverse entry that accidentally became relevant
    /// would make <c>PollutionRate</c> mean something different in the two arms and the comparison
    /// worthless.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Diverse_noise_is_never_relevant_to_any_query(CorpusLanguage language)
    {
        var corpus = MemoryCorpus.Generate(
            Shape.Default with { Language = language, NoiseKind = CorpusNoiseKind.Diverse }, seed: 3);

        var relevant = corpus.Steps.OfType<CorpusQuery>().SelectMany(q => q.RelevantIds).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(relevant, id => id.StartsWith("noise", StringComparison.Ordinal));
    }

    /// <summary>Both emission sites honour the axis. Noise is written in two places — spread across the
    /// units, then topped up at the end — and a run where only the first branch was converted would still
    /// look "diverse" while quietly diluting the contrast with templated entries. Counted rather than
    /// sampled, because the top-up is exactly the tail a spot check misses.</summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public void Every_noise_entry_is_diverse_not_only_the_ones_written_in_the_main_loop(CorpusLanguage language)
    {
        var corpus = MemoryCorpus.Generate(
            Shape.Default with { Language = language, NoiseDensity = 40, NoiseKind = CorpusNoiseKind.Diverse },
            seed: 9);
        var lex = CorpusLexicon.For(language);

        var stillTemplated = NoiseTexts(corpus)
            .Where(t => t.Contains(lex.Noise("PROBE", lex.Fillers[0]).Split(' ')[^1], StringComparison.Ordinal))
            .ToList();

        Assert.Empty(stillTemplated);
    }

    private static List<string> NoiseTexts(MemoryCorpus corpus) =>
        [.. corpus.Steps.OfType<CorpusWrite>()
            .Select(w => w.Write.Content)
            .Where(c => c.Contains("noise", StringComparison.Ordinal))];

    /// <summary>Mean pairwise Jaccard over the library's OWN tokenization — asking the overlap question
    /// with a different split than the store uses would answer a different question.</summary>
    private static double MeanJaccard(IReadOnlyList<string> texts)
    {
        var sets = texts.Select(t => Lyntai.Storage.SearchTerms.Extract(t).ToHashSet(StringComparer.Ordinal)).ToList();
        double total = 0;
        var pairs = 0;
        for (var i = 0; i < sets.Count; i++)
            for (var j = i + 1; j < sets.Count; j++)
            {
                var union = sets[i].Count + sets[j].Count - sets[i].Count(sets[j].Contains);
                total += union == 0 ? 0 : (double)sets[i].Count(sets[j].Contains) / union;
                pairs++;
            }
        return pairs == 0 ? 0 : total / pairs;
    }

    private static string Render(MemoryCorpus corpus) =>
        string.Join('\n', corpus.Steps.Select(s => s switch
        {
            CorpusWrite w => $"W|{w.Write.Content}",
            CorpusQuery q => $"Q|{q.Text}|{string.Join(',', q.RelevantIds)}",
            CorpusExpand e => $"E|{e.EntryId}",
            _ => s.ToString(),
        }));
}
