using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Verification;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b>What a verdict does to the RESULT when <c>VerificationFilters</c> is false</b> — which is the default,
/// and the setting that option's own docs recommend.
///
/// <para>Filed by an adopter who benchmarked a judge, saw no change, and read the code as saying a verdict
/// cannot reach the ranking at all. Both readings that report went through are reachable from the source and
/// both are wrong, which is why these are FACTS rather than a paragraph: a verdict promotes every endorsed
/// candidate to the front of the ordinary set BEFORE the caller's limit is applied, so it can move a result
/// and can rescue one that never fitted on the page. What <c>VerificationFilters</c> adds on top is
/// REMOVAL.</para>
///
/// <para><b>Each arm runs on its OWN database.</b> A recall reinforces what it returns and links what it
/// returns together, so running a baseline and then an arm against one store compares a cold graph with one
/// the baseline warmed — the same trap the adopter hit, one level down.</para>
/// </summary>
public class MemoryVerificationOrderingTests
{
    private const string Query = "deployment note";

    private static readonly string[] Facts =
    [
        "deployment note one covers response caching",
        "deployment note two covers index rebuilds",
        "deployment note three covers schema migrations",
        "deployment note four covers rollback drills",
    ];

    /// <summary>Endorses whichever candidates carry <paramref name="headline"/> — keyed on the text rather
    /// than on an id, so a fixture states WHICH entry it is endorsing instead of a store-assigned number.
    /// </summary>
    private sealed class Endorses(string headline) : IMemoryVerificationPolicy
    {
        public Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request,
            CancellationToken ct = default)
        {
            var hits = request.Candidates
                .Where(c => string.Equals(c.Headline, headline, StringComparison.Ordinal))
                .Select(c => c.Id)
                .ToList();
            return Task.FromResult(hits.Count == 0
                ? MemoryVerification.NothingRelevant
                : new MemoryVerification(hits));
        }
    }

    private static async Task<IReadOnlyList<string>> RecallAsync(
        TempDb db, IMemoryVerificationPolicy? verifier, int limit)
    {
        var engine = new GraphMemoryEngine("verify", new SqliteMemoryGraphStore(db.Factory),
            agePolicies: [new PerWriteAgePolicy()],
            retrievability: new DsrRetrievability(), ranking: new ReciprocalRankFusionPolicy(),
            verification: verifier);

        foreach (var fact in Facts) await engine.RememberAsync(new MemoryWrite("t", "s", fact));

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", Query, Limit: limit));
        return [.. recall.Items.Select(i => i.Headline ?? string.Empty)];
    }

    /// <summary>The baseline order, on a store no other arm has touched.</summary>
    private static async Task<IReadOnlyList<string>> BaselineAsync()
    {
        using var db = new TempDb();
        return await RecallAsync(db, verifier: null, limit: 10);
    }

    /// <summary><b>A verdict moves a result with filtering OFF.</b> Endorsing the third-ranked entry alone
    /// brings it to the front of the same page — no option was set, and nothing was removed.</summary>
    [Fact]
    public async Task An_endorsed_candidate_leads_the_page_with_filtering_off()
    {
        var baseline = await BaselineAsync();
        Assert.Equal(4, baseline.Count);

        using var db = new TempDb();
        var judged = await RecallAsync(db, new Endorses(baseline[2]), limit: 10);

        Assert.Equal(baseline[2], judged[0]);
        Assert.Equal(baseline.Count, judged.Count);                       // promoted, never filtered
        Assert.Equal([baseline[0], baseline[1], baseline[3]], judged.Skip(1));  // the rest keep their order
    }

    /// <summary><b>…and it can rescue one that never fitted on the page.</b> The promotion happens before the
    /// caller's limit is applied and the judge is shown <c>VerificationDepth</c> candidates, so the
    /// LAST-ranked entry reaches a page of two. This is the effect the option is worth having for, and it is
    /// invisible to a benchmark that only compares the entries already returned.</summary>
    [Fact]
    public async Task An_endorsed_candidate_below_the_limit_is_rescued_onto_the_page()
    {
        var baseline = await BaselineAsync();

        using var db = new TempDb();
        var judged = await RecallAsync(db, new Endorses(baseline[3]), limit: 2);

        Assert.Equal(2, judged.Count);
        Assert.Equal(baseline[3], judged[0]);
    }

    /// <summary><b>The adopter's null result, reproduced deterministically.</b> A judge that endorses what
    /// already leads changes nothing at all — which is what a good judge does on a corpus the lexical ranker
    /// already answers, and is NOT evidence that a verdict cannot move a result. Without this fact the two
    /// above read as "verification always reorders", which would be the third wrong reading.</summary>
    [Fact]
    public async Task A_judge_that_endorses_what_already_leads_returns_an_identical_page()
    {
        var baseline = await BaselineAsync();

        using var db = new TempDb();
        var judged = await RecallAsync(db, new Endorses(baseline[0]), limit: 10);

        Assert.Equal(baseline, judged);
    }
}
