using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Xunit;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>
/// Phase 2 of the memory proposal, as PROPERTIES over a generated timeline rather than as a sweep.
///
/// <para><b>Why not a sweep.</b> Phase 2 sets hard targets of <c>100%</c> temporal accuracy, <c>0</c>
/// stale answers and <c>0</c> hidden conflicts on a deterministic corpus. A metric pinned at 100% with no
/// arm to compare against is a TEST — there is no trade-off to price and nothing for a reader to decide, so
/// a bench command printing <c>100.0%</c> would be ceremony. What earns its keep instead is BREADTH: the
/// same properties over many generated timelines, where the interactions live.</para>
///
/// <para><b>What these are held to is <c>docs/DECISIONS.md</c> D90</b>, which put "nothing is silently
/// overwritten and no conflict is hidden" and "current and historical facts resolve correctly by time"
/// above the optimization targets as invariants. That is what makes a failure here a rejection rather than
/// a number to weigh — and it is what forbids the cheapest way to pass: "newest recorded wins" resolves
/// every dispute and satisfies none of it.</para>
/// </summary>
public class TemporalResolutionTests
{
    private const string Engine = "temporal";

    /// <summary>Twelve timelines. Enough that a defect needing a particular interleaving shows up, few
    /// enough that the suite stays fast — each seed is ~30 writes against an in-process store.</summary>
    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var i = 0; i < 12; i++) data.Add(9000 + i);
        return data;
    }

    private static async Task<(IMemoryGraphStore Store, TemporalCorpus.Corpus Corpus)> WriteAsync(int seed)
    {
        var corpus = TemporalCorpus.Generate(seed);
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine(Engine, store, agePolicies: [new PerWriteAgePolicy()]);

        foreach (var claim in corpus.Claims)
            await engine.RememberAsync(TemporalCorpus.ToWrite(claim));

        return (store, corpus);
    }

    /// <summary>The instants each timeline is probed at — before it starts, and across its whole span.</summary>
    private static IEnumerable<DateTimeOffset> Probes(TemporalCorpus.Corpus corpus)
    {
        yield return corpus.Epoch.AddDays(-1);          // before anything is in force
        for (var month = 0; month <= 12; month += 2) yield return corpus.Epoch.AddMonths(month).AddDays(15);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task Temporal_exact_accuracy_is_total_and_no_answer_is_stale(int seed)
    {
        // TEMPORAL EXACT ACCURACY = 100% and STALE-ANSWER RATE = 0, which on a deterministic corpus are the
        // same assertion read from two directions: returning exactly the version(s) in force at an instant
        // is what "not stale" means. Ground truth is computed from the claim list by DEFINITION, never by
        // re-running the resolver's own rule, or the comparison would be a tautology.
        var (store, corpus) = await WriteAsync(seed);

        foreach (var key in corpus.Keys)
        {
            foreach (var asOf in Probes(corpus))
            {
                var history = await AssertionResolver.HistoryAsync(store, Engine, "t", "s", key, asOf);
                var actual = AssertionResolver.CurrentOf(history)
                    .Select(a => a.Content).Order(StringComparer.Ordinal).ToList();

                Assert.Equal(TemporalCorpus.ExpectedAt(corpus, key, asOf), actual);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task History_completeness_is_total_so_no_version_is_lost_behind_a_successor(int seed)
    {
        // "No canonical evidence is silently lost" (D90 invariant 2) applied to supersession: a version
        // that stopped being current is still evidence, and must remain in its key's history. This is the
        // half a current-answer test cannot see -- a resolver that DELETED superseded rows would pass every
        // accuracy probe above.
        var (store, corpus) = await WriteAsync(seed);

        foreach (var key in corpus.Keys)
        {
            var expected = corpus.Claims.Where(c => c.CanonicalKey == key)
                .Select(c => c.Content).Order(StringComparer.Ordinal).ToList();

            // Probed at the END of the timeline, where every version has been superseded by a later one
            var history = await AssertionResolver.HistoryAsync(
                store, Engine, "t", "s", key, corpus.Epoch.AddYears(5));

            Assert.Equal(expected, history.Select(a => a.Content).Order(StringComparer.Ordinal).ToList());
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task Hidden_conflict_rate_is_zero_in_BOTH_directions(int seed)
    {
        // HIDDEN-CONFLICT RATE = 0 needs both halves, and only one of them is the headline. A contested key
        // must return every rival rather than a silent winner -- and an UNCONTESTED key must return exactly
        // one, or a resolver that called everything disputed would score a perfect zero on the first half
        // while being useless. A rule that also fires when nothing is wrong is worse than no rule.
        var (store, corpus) = await WriteAsync(seed);

        foreach (var key in corpus.Keys)
        {
            var asOf = corpus.Epoch.AddYears(5);
            var current = AssertionResolver.CurrentOf(
                await AssertionResolver.HistoryAsync(store, Engine, "t", "s", key, asOf));

            if (corpus.Disputed.Contains(key))
            {
                Assert.True(current.Count > 1,
                    $"[seed {seed}] {key} is contested and resolved to a single answer — a silent pick");
                Assert.All(current, a =>
                    Assert.Equal(AssertionResolver.Status.Disputed, a.State));
                Assert.Equal(2, current.Select(a => a.SourceRef).Distinct(StringComparer.Ordinal).Count());
            }
            else
            {
                Assert.True(current.Count == 1,
                    $"[seed {seed}] {key} is uncontested and returned {current.Count} answers");
                Assert.Equal(AssertionResolver.Status.Active, current[0].State);
            }
        }
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task Source_lineage_is_complete_so_every_answer_says_where_it_came_from(int seed)
    {
        // SOURCE-LINEAGE COVERAGE = 100%. Worth its own fact rather than folding into the others, because
        // it is the property a DISPUTE is useless without: "these two disagree" is only actionable if the
        // caller can see who said each one.
        var (store, corpus) = await WriteAsync(seed);

        foreach (var key in corpus.Keys)
        {
            var history = await AssertionResolver.HistoryAsync(
                store, Engine, "t", "s", key, corpus.Epoch.AddYears(5));

            Assert.NotEmpty(history);
            Assert.All(history, a =>
            {
                Assert.False(string.IsNullOrEmpty(a.SourceRef));
                Assert.NotEqual(default, a.RecordedAt);
            });
        }
    }

    [Fact]
    public async Task The_corpus_actually_contains_what_it_claims_to()
    {
        // THE INSTRUMENT'S OWN CONTROL, and the reason it is here rather than assumed: every property above
        // passes vacuously on a corpus that generated no disputes, no multi-version keys, or that silently
        // deduplicated two versions into one row because their content collided. An instrument is code
        // nothing else validates, and its output is a pass, which is never obviously wrong.
        var seeds = Enumerable.Range(9000, 12).ToList();
        var disputes = 0;
        var multiVersion = 0;

        foreach (var seed in seeds)
        {
            var (store, corpus) = await WriteAsync(seed);
            disputes += corpus.Disputed.Count;
            multiVersion += corpus.Keys.Count(k => corpus.Claims.Count(c => c.CanonicalKey == k) > 1);

            // every generated claim reached the store as its OWN row — content collisions would merge them
            var stored = await store.SeedAsync(Engine, "t", "s", null, int.MaxValue);
            Assert.Equal(corpus.Claims.Count, stored.Count);

            // ...and a late-arriving version is present, or the bitemporal half is untested
            Assert.Contains(corpus.Claims, c => c.RecordedAt > c.ValidFrom);
        }

        Assert.True(disputes > 0, "no seed generated a dispute; the conflict properties are vacuous");
        Assert.True(multiVersion > 0, "no key had two versions; the supersession properties are vacuous");
    }
}
