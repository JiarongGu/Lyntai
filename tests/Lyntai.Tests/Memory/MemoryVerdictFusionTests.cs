using Lyntai.Memory;
using Lyntai.Memory.Verification;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>
/// <b><see cref="GraphMemoryOptions.VerdictCombination"/> — whether a verdict PARTITIONS or COMPETES.</b>
///
/// <para>The partition is the shipped default and promotes every endorsed candidate ahead of every
/// unendorsed one. That is the only place this engine combines a signal by partition rather than by rank
/// competition, and it is why a weak judge can cost more than no judge at all: an endorsement set larger
/// than the page REPLACES the ranking instead of refining it. Measured on LoCoMo with a real 4B judge —
/// −10.5 points partitioned, and exactly its unjudged base when fused (<c>docs/memory-measurements.md</c> §5).</para>
///
/// <para>The fixture — and the reason each arm runs on its own database — is
/// <see cref="VerdictOrderingFixture"/>, shared with <see cref="MemoryVerificationOrderingTests"/>.</para>
/// </summary>
public class MemoryVerdictFusionTests
{
    private static Task<IReadOnlyList<string>> RecallAsync(TempDb db, IMemoryVerificationPolicy? verifier,
        int limit, GraphMemoryOptions? options = null) =>
        VerdictOrderingFixture.RecallAsync(db, verifier, limit, options);

    private static Task<IReadOnlyList<string>> BaselineAsync() => VerdictOrderingFixture.BaselineAsync();

    private static VerdictOrderingFixture.Endorses Endorses(string headline) => new(headline);

    private static GraphMemoryOptions Fused() =>
        new() { VerdictCombination = MemoryVerdictCombination.Fuse };

    /// <summary><b>The default is the partition</b>, stated as a fact rather than left to the property
    /// initializer, because this option existing at all is only safe if adopting it is a CHOICE.</summary>
    [Fact]
    public void The_default_combination_is_the_partition()
        => Assert.Equal(MemoryVerdictCombination.Partition, new GraphMemoryOptions().VerdictCombination);

    /// <summary><b>Fused, an endorsement no longer beats the ranking outright.</b> Endorsing the LAST-ranked
    /// of four puts it second — its own rank still counts against it — where the partition puts it first.
    ///
    /// <para><b>This is also the DEGENERATE control the bench's own first attempt failed.</b> Giving an
    /// unendorsed candidate a zero verdict term treats it as UNRANKED, which makes the worst endorsed
    /// candidate outscore the best unendorsed one at every rank — arithmetically the partition again, and
    /// indistinguishable from it in every score column. An unlisted id is judged NOT to have answered, which
    /// is a LOW RANK and not an absence, so this test fails if that mistake is repeated.</para></summary>
    [Fact]
    public async Task Fused_a_poorly_ranked_endorsement_does_not_displace_the_leader()
    {
        var baseline = await BaselineAsync();
        Assert.Equal(4, baseline.Count);

        using var partitioned = new TempDb();
        var byPartition = await RecallAsync(partitioned, Endorses(baseline[3]), limit: 10);
        Assert.Equal(baseline[3], byPartition[0]);              // the shipped behaviour, for contrast

        using var db = new TempDb();
        var fused = await RecallAsync(db, Endorses(baseline[3]), limit: 10, Fused());

        Assert.Equal(baseline[0], fused[0]);                    // the ranking's leader survives the verdict
        Assert.Equal(baseline[3], fused[1]);                    // the endorsement still gains three places
        Assert.Equal(baseline.Count, fused.Count);              // promotion, never filtering
    }

    /// <summary><b>…and the RESCUE survives fusion, which is what makes it safe to adopt.</b> The endorsed
    /// last-ranked entry still reaches a page of two. Fusion is meant to stop a verdict REPLACING the
    /// ranking, not to stop it working: an arm that lost the rescue would be a worse judge seam, not a
    /// safer one.</summary>
    [Fact]
    public async Task Fused_an_endorsed_candidate_below_the_limit_is_still_rescued_onto_the_page()
    {
        var baseline = await BaselineAsync();

        using var db = new TempDb();
        var fused = await RecallAsync(db, Endorses(baseline[3]), limit: 2, Fused());

        Assert.Equal(2, fused.Count);
        Assert.Contains(baseline[3], fused);
    }

    /// <summary><b>A judge that endorses what already leads still changes nothing.</b> The null result has to
    /// survive the new combination too — otherwise fusion would be reordering on its own account, which is
    /// the opposite of what it is for.</summary>
    [Fact]
    public async Task Fused_a_judge_that_endorses_the_leader_returns_an_identical_page()
    {
        var baseline = await BaselineAsync();

        using var db = new TempDb();
        var fused = await RecallAsync(db, Endorses(baseline[0]), limit: 10, Fused());

        Assert.Equal(baseline, fused);
    }

    /// <summary><b>No verdict, no reordering — under EITHER combination.</b> The control that proves the
    /// option is inert without a judge, so a deployment that sets it and registers no verifier has changed
    /// nothing.</summary>
    [Fact]
    public async Task Without_a_verifier_the_combination_changes_nothing()
    {
        var baseline = await BaselineAsync();

        using var db = new TempDb();
        Assert.Equal(baseline, await RecallAsync(db, verifier: null, limit: 10, Fused()));
    }
}
