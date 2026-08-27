using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Tests.Memory;

/// <summary><b>Does reinforcing only what a caller PAID for beat reinforcing whatever the ranker
/// returned?</b> `TASKS.md` Part 64's design-audit item, and the half of reinforcement the effect seam
/// (<b>D57</b>) deliberately did not answer.
///
/// <para><b>The premise error being tested.</b> The README promises that <i>material you keep coming back
/// to</i> becomes durable; the implementation reinforces whatever the ranker RETURNED. Those were treated as
/// the same thing for the subsystem's whole life and are not. FSRS's "retrieval strengthens memory" comes
/// from a domain where retrieval is VERIFIED — the learner knows whether they got it right. Here it is
/// asserted by the same ranker being reinforced, so the loop upvotes its own prior, mistakes included.</para>
///
/// <para><b>Why this engine can even ask.</b> <c>RecallAsync</c> returns speculative headlines;
/// <c>ExpandAsync</c> is a caller choosing to pay for full content — literally "coming back to it". Both
/// reinforced with identical weight, so the discriminating signal was produced and discarded.</para>
///
/// <para><b>Growth is switched ON at the policy for every arm here</b> (<c>ReinforceGain = 2.0</c>). 3.0
/// ships growth-free (<b>D54</b>), under which every arm below would be identical by construction and the
/// comparison would measure nothing. This asks what the act gate is worth to a deployment that has
/// deliberately turned growth back on — the only deployment for which the question exists.</para></summary>
public sealed class MemoryReinforcementActTests
{
    private const int Seed = 909;
    private const int QueryLimit = 10;

    private static GraphMemoryEngine NewEngine(InMemoryMemoryGraphStore store, MemoryReinforcementActs acts,
        double gain) =>
        new("e", store,
            retrievability: new DsrRetrievability(new DsrOptions { ReinforceGain = gain }),
            agePolicies: [new PerWriteAgePolicy()],
            options: new GraphMemoryOptions { ReinforceOn = acts });

    /// <summary>3.0's shipped growth setting (<b>D54</b>): retrieval grows no stability. Under it the act
    /// gate still governs the AGE RESET and co-activation, so the question remains live — it just has a
    /// different answer, which is why both are measured rather than one being assumed to stand in for the
    /// other.</summary>
    private const double ShippedGain = 0;

    /// <summary>Growth deliberately turned back on — the only configuration for which "does the act matter"
    /// is a question about stability at all.</summary>
    private const double GrowthOnGain = 2.0;

    private readonly record struct Arm(double Miss, double Pollution, int Expansions);

    private static async Task<Arm> RunAsync(MemoryReinforcementActs acts, double gain)
    {
        // ExpandRatio > 0 is what makes the expansion act reachable at all — before CorpusExpand existed,
        // every measurement ever taken against this engine exercised reinforcement-on-recall ONLY.
        var shape = CorpusShape.Default with { ExpandRatio = 3 };
        var corpus = MemoryCorpus.Generate(shape, Seed);
        var store = new InMemoryMemoryGraphStore();
        var engine = NewEngine(store, acts, gain);
        var first = corpus.Steps.OfType<CorpusWrite>().First().Write;

        var byCorpusId = new Dictionary<string, string>(StringComparer.Ordinal);
        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        long returned = 0, noise = 0, wanted = 0, missed = 0;
        var expansions = 0;

        foreach (var step in corpus.Steps)
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = await engine.RememberAsync(w.Write);
                    var corpusId = MemoryCorpusTestAccess.IdOf(w.Write.Content);
                    byCorpusId[corpusId] = memRef.Id;
                    byRef[memRef.Id] = corpusId;
                    break;

                case CorpusQuery q:
                    var recall = await engine.RecallAsync(
                        new MemoryQuery(first.TaskKey, first.Scope, q.Text, Limit: QueryLimit));
                    var got = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in recall.Items)
                    {
                        returned++;
                        if (!byRef.TryGetValue(item.Reference.Id, out var id)) continue;
                        got.Add(id);
                        if (id.StartsWith("noise", StringComparison.Ordinal)) noise++;
                    }
                    foreach (var want in q.RelevantIds)
                    {
                        wanted++;
                        if (!got.Contains(want)) missed++;
                    }
                    break;

                case CorpusExpand e:
                    if (byCorpusId.TryGetValue(e.EntryId, out var refId))
                    {
                        await engine.ExpandAsync(new MemoryRef("e", refId));
                        expansions++;
                    }
                    break;
            }

        return new Arm(
            wanted == 0 ? 0 : (double)missed / wanted,
            returned == 0 ? 0 : (double)noise / returned,
            expansions);
    }

    /// <summary><b>THE MEASUREMENT.</b> Four arms over the same corpus and seed, differing only in which act
    /// reinforces.
    /// <para>The expansion count is asserted first: if the corpus produced no expansions, three of the four
    /// arms are the same engine and every delta below is zero for a reason that has nothing to do with the
    /// hypothesis — the same "control identical to treatment" failure the salience study hit
    /// (<c>TASKS.md</c> Part 69).</para></summary>
    [Fact]
    public async Task Conditioning_reinforcement_on_expansion_is_measured_rather_than_assumed()
    {
        var both = await RunAsync(MemoryReinforcementActs.All, GrowthOnGain);
        var recallOnly = await RunAsync(MemoryReinforcementActs.Recall, GrowthOnGain);
        var expansionOnly = await RunAsync(MemoryReinforcementActs.Expansion, GrowthOnGain);
        var neither = await RunAsync(MemoryReinforcementActs.None, GrowthOnGain);

        var sBoth = await RunAsync(MemoryReinforcementActs.All, ShippedGain);
        var sRecall = await RunAsync(MemoryReinforcementActs.Recall, ShippedGain);
        var sExpansion = await RunAsync(MemoryReinforcementActs.Expansion, ShippedGain);
        var sNeither = await RunAsync(MemoryReinforcementActs.None, ShippedGain);

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
                                   growth ON (2.0)        growth OFF (shipped)
             reinforcement act     miss     pollution     miss     pollution
               both (default)      {both.Miss:F4}   {both.Pollution:F4}        {sBoth.Miss:F4}   {sBoth.Pollution:F4}
               recall only         {recallOnly.Miss:F4}   {recallOnly.Pollution:F4}        {sRecall.Miss:F4}   {sRecall.Pollution:F4}
               expansion only      {expansionOnly.Miss:F4}   {expansionOnly.Pollution:F4}        {sExpansion.Miss:F4}   {sExpansion.Pollution:F4}
               neither             {neither.Miss:F4}   {neither.Pollution:F4}        {sNeither.Miss:F4}   {sNeither.Pollution:F4}
               expansions replayed: {both.Expansions}
             """);

        Assert.True(both.Expansions > 0,
            $"no expansions were replayed, so three of these four arms are the same engine:\n{table}");

        // (1) THE RESULT, and it holds in BOTH growth configurations rather than only the one that makes
        //     the effect largest — which is what makes it a finding about the ACT rather than about gain.
        Assert.True(expansionOnly.Miss < both.Miss && expansionOnly.Pollution < both.Pollution,
            $"expansion-only should beat the default on both metrics (growth ON):\n{table}");
        Assert.True(sExpansion.Miss < sBoth.Miss && sExpansion.Pollution < sBoth.Pollution,
            $"expansion-only should beat the default on both metrics (growth OFF, shipped):\n{table}");

        // (2) The half that REFUTES "less reinforcement is always better" (`TASKS.md` Part 64's earlier
        //     reading). Expansion-only reinforces MORE than `neither` and is better on both metrics — so the
        //     damage was never the quantity, it was the SIGNAL.
        Assert.True(expansionOnly.Pollution < neither.Pollution,
            $"expansion reinforcement must do positive work, or the finding is just 'reinforce less':\n{table}");
        Assert.True(expansionOnly.Miss < neither.Miss,
            $"expansion-only should also beat doing nothing on miss:\n{table}");

        // (3) Recall dominates the default: `both` and `recall only` land in the same place, so essentially
        //     all of the shipped configuration's cost comes from reinforcing the ranker's own output.
        Assert.True(Math.Abs(both.Miss - recallOnly.Miss) < 0.02,
            $"`both` and `recall only` should be near-identical, showing recall dominates:\n{table}");
    }

    /// <summary>The act gate actually gates: with <see cref="MemoryReinforcementActs.Expansion"/> alone, a
    /// recall leaves what it returned untouched while an expansion still resets the entry it opened.
    /// <para>Positive on both sides. A test asserting only that a recall changed nothing would pass equally
    /// if the recall returned nothing at all, so the expansion half is what proves the engine is still
    /// reinforcing something.</para></summary>
    [Fact]
    public async Task Expansion_only_leaves_a_recall_untouched_but_still_reinforces_what_was_opened()
    {
        const string content = "the deploy pipeline requires manual approval";
        var store = new InMemoryMemoryGraphStore();
        var engine = NewEngine(store, MemoryReinforcementActs.Expansion, GrowthOnGain);

        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", content));
        var id = long.Parse(reference.Id, CultureInfo.InvariantCulture);
        for (var i = 0; i < 10; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"unrelated filler entry number {i}"));

        var beforeRecall = (await store.GetAsync("e", id))!;
        Assert.NotEmpty((await engine.RecallAsync(new MemoryQuery("t", "s", "deploy pipeline"))).Items);
        var afterRecall = (await store.GetAsync("e", id))!;

        Assert.Equal(beforeRecall.OrdinalAge, afterRecall.OrdinalAge, precision: 9);
        Assert.Equal(beforeRecall.Stability, afterRecall.Stability, precision: 9);

        await engine.ExpandAsync(reference);
        var afterExpand = (await store.GetAsync("e", id))!;

        Assert.Equal(0, afterExpand.OrdinalAge, precision: 9);
        Assert.True(afterExpand.Stability > beforeRecall.Stability,
            $"expansion must still reinforce; was {beforeRecall.Stability}, now {afterExpand.Stability}");
    }

    /// <summary>The mirror: <see cref="MemoryReinforcementActs.Recall"/> alone leaves an EXPANSION
    /// untouched. Without this the fact above could be satisfied by an engine that simply reinforces less in
    /// general, rather than by one that distinguishes the two acts.</summary>
    [Fact]
    public async Task Recall_only_leaves_an_expansion_untouched()
    {
        const string content = "the deploy pipeline requires manual approval";
        var store = new InMemoryMemoryGraphStore();
        var engine = NewEngine(store, MemoryReinforcementActs.Recall, GrowthOnGain);

        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", content));
        var id = long.Parse(reference.Id, CultureInfo.InvariantCulture);
        for (var i = 0; i < 10; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"unrelated filler entry number {i}"));

        var before = (await store.GetAsync("e", id))!;
        await engine.ExpandAsync(reference);
        var after = (await store.GetAsync("e", id))!;

        Assert.Equal(before.OrdinalAge, after.OrdinalAge, precision: 9);
        Assert.Equal(before.Stability, after.Stability, precision: 9);
    }

    /// <summary><b>Why the DEFAULT stays <see cref="MemoryReinforcementActs.All"/> despite expansion-only
    /// winning every cell.</b> Recorded as a fact rather than a comment because it is the one thing a reader
    /// of the numbers above will want to overturn.
    ///
    /// <para>An application that never calls <c>ExpandAsync</c> gets the <c>neither</c> arm under an
    /// expansion-only default — the WORST pollution measured (0.4118 against the default's 0.3331). So the
    /// change that helps an expanding consumer silently degrades a non-expanding one, and nothing in the
    /// library knows which it is talking to. `AddMemoryTools` exposes expand to a model, so agentic
    /// consumers do expand; a consumer calling <c>RecallAsync</c> directly may never.</para>
    ///
    /// <para>This test pins that trade rather than describing it: it asserts the non-expanding case is worse
    /// under an expansion-only setting, which is the entire reason the default did not move.</para></summary>
    [Fact]
    public async Task Expansion_only_would_silently_degrade_an_application_that_never_expands()
    {
        var shape = CorpusShape.Default; // ExpandRatio 0 — no expansions at all
        var corpus = MemoryCorpus.Generate(shape, Seed);
        Assert.Empty(corpus.Steps.OfType<CorpusExpand>());

        var withDefault = await RunNoExpandAsync(MemoryReinforcementActs.All, corpus);
        var withExpansionOnly = await RunNoExpandAsync(MemoryReinforcementActs.Expansion, corpus);

        Assert.True(withExpansionOnly.Pollution > withDefault.Pollution,
            $"""
             expansion-only should be WORSE for a non-expanding application — that is why the default did not
             move. default pollution {withDefault.Pollution:F4}, expansion-only {withExpansionOnly.Pollution:F4}
             """);
    }

    private static async Task<Arm> RunNoExpandAsync(MemoryReinforcementActs acts, MemoryCorpus corpus)
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = NewEngine(store, acts, ShippedGain);
        var first = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        long returned = 0, noise = 0, wanted = 0, missed = 0;

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
                    var got = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in recall.Items)
                    {
                        returned++;
                        if (!byRef.TryGetValue(item.Reference.Id, out var id)) continue;
                        got.Add(id);
                        if (id.StartsWith("noise", StringComparison.Ordinal)) noise++;
                    }
                    foreach (var want in q.RelevantIds)
                    {
                        wanted++;
                        if (!got.Contains(want)) missed++;
                    }
                    break;
            }

        return new Arm(
            wanted == 0 ? 0 : (double)missed / wanted,
            returned == 0 ? 0 : (double)noise / returned,
            0);
    }
}
