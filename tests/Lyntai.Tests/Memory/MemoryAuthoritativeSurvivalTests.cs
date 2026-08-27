using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Memory.Corpus;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>Objective (1): never lose an authoritative fact — measured end to end for the first time.</b>
///
/// <para>Design §5.7.0 orders the memory engine's goals lexicographically, and objective (1) is the only one
/// with NO acceptable failure rate. Until <see cref="CorpusShape.AuthoritativeCount"/> existed the corpus
/// held zero <see cref="MemoryGrade"/> references, so every number this project has ever published was about
/// objectives (2) and (3): a null result on (1) meant "not exercised", never "kept". The store-level contract
/// facts cover ADMISSION — that <c>SeedAsync</c>'s grade carve-out puts an exact fact into the candidate set
/// — and say nothing about whether it survives ranking, the limit, and a corpus-long interference gap.</para>
///
/// <para><b>Why this is a guard rather than a sweep.</b> Objective (1) has no acceptable failure rate, so its
/// target is a constant, not a distribution: any miss is a defect. There is nothing to average and nothing
/// to compare arms on — which is exactly why it belongs in the test suite where a regression fails the build,
/// rather than in a bench command someone remembers to run.</para>
/// </summary>
public class MemoryAuthoritativeSurvivalTests
{
    private const int Seed = 12345;
    private const int QueryLimit = 10;

    private static GraphMemoryEngine NewEngine(TempDb db) =>
        new("authoritative", new SqliteMemoryGraphStore(db.Factory),
            agePolicies: [new PerWriteAgePolicy()],
            retrievability: new DsrRetrievability(), ranking: new ReciprocalRankFusionPolicy());

    /// <summary><b>Every authoritative fact comes back, in every language, from a query that singles out
    /// none of them.</b>
    /// <para>The facts are written FIRST, so by the time the probe runs they are the oldest material in the
    /// corpus and have sat through every other write — an entry that "never decays" has to be shown not
    /// decaying, and burying it is how. The probe carries only the shared leading token every entry has, so
    /// the authoritative facts compete on exactly the same weak footing as ~260 others: under a limit of 10
    /// nothing but the grade carve-out can return them.</para></summary>
    [Theory]
    [MemberData(nameof(MemoryCorpusTests.Languages), MemberType = typeof(MemoryCorpusTests))]
    public async Task An_authoritative_fact_survives_the_whole_corpus(CorpusLanguage language)
    {
        var corpus = MemoryCorpus.Generate(
            CorpusShape.Default with { AuthoritativeCount = 3, Language = language }, Seed);

        using var db = new TempDb();
        var engine = NewEngine(db);
        var first = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        var probes = new List<(IReadOnlyList<string> Relevant, List<string> Got)>();

        foreach (var step in corpus.Steps)
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = await engine.RememberAsync(w.Write);
                    byRef[memRef.Id] = MemoryCorpusTestAccess.IdOf(w.Write.Content);
                    break;

                case CorpusQuery q:
                    var recall = await engine.RecallAsync(
                        new MemoryQuery(first.TaskKey, first.Scope, q.Text, Limit: QueryLimit));
                    var got = recall.Items
                        .Select(i => byRef.TryGetValue(i.Reference.Id, out var c) ? c : i.Reference.Id)
                        .ToList();
                    if (q.RelevantIds.Count > 0
                        && q.RelevantIds.All(id => id.StartsWith("authoritative", StringComparison.Ordinal)))
                        probes.Add((q.RelevantIds, got));
                    break;
            }

        Assert.NotEmpty(probes);
        foreach (var (relevant, got) in probes)
        {
            var lost = relevant.Except(got, StringComparer.Ordinal).ToList();
            Assert.True(lost.Count == 0,
                $"[{language}] objective (1) BROKEN — authoritative fact(s) {string.Join(", ", lost)} were " +
                $"not returned. This is the one promise with no acceptable failure rate. Returned: " +
                string.Join(", ", got));
        }
    }

    /// <summary><b>The control that stops the fact above passing for the wrong reason.</b> If the probe
    /// returned these entries because it MATCHED them rather than because they were graded, the test would be
    /// an ordinary keyword recall wearing objective (1)'s name. Writing the identical corpus with the grade
    /// removed must lose them.</summary>
    [Fact]
    public async Task The_same_facts_are_lost_without_the_grade()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default with { AuthoritativeCount = 3 }, Seed);

        using var db = new TempDb();
        var engine = NewEngine(db);
        var first = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        List<string>? lastProbe = null;
        IReadOnlyList<string> relevant = [];

        foreach (var step in corpus.Steps)
            switch (step)
            {
                case CorpusWrite w:
                    // the ONE difference from the fact above: the grade is dropped
                    var plain = w.Write with { Grade = MemoryGrade.Inherit };
                    byRef[(await engine.RememberAsync(plain)).Id] = MemoryCorpusTestAccess.IdOf(plain.Content);
                    break;

                case CorpusQuery q:
                    var recall = await engine.RecallAsync(
                        new MemoryQuery(first.TaskKey, first.Scope, q.Text, Limit: QueryLimit));
                    if (q.RelevantIds.Count > 0
                        && q.RelevantIds.All(id => id.StartsWith("authoritative", StringComparison.Ordinal)))
                    {
                        relevant = q.RelevantIds;
                        lastProbe = [.. recall.Items.Select(i =>
                            byRef.TryGetValue(i.Reference.Id, out var c) ? c : i.Reference.Id)];
                    }
                    break;
            }

        Assert.NotNull(lastProbe);
        Assert.NotEmpty(relevant);
        Assert.DoesNotContain(lastProbe, id => relevant.Contains(id, StringComparer.Ordinal));
    }
}
