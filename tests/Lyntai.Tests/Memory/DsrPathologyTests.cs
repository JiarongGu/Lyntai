using System.Globalization;
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
/// A pathology battery for <see cref="DsrRetrievability"/> — the standing evidence that the curve this
/// library ships as its ONLY forgetting model does not do anything unacceptable. Every item is a
/// falsification check on DSR itself, not a comparison against another curve.
/// <para><b>The battery, enumerated before any of it ran</b> — an enumerated-in-advance list is what makes a
/// "did not falsify" claim credible rather than a search for facts that happened to pass:</para>
/// <list type="number">
/// <item>A memory becoming permanently UNREACHABLE. Burial is fine; unreachability is not.</item>
/// <item>Stability COLLAPSING or EXPLODING under a reachable sequence of writes and recalls.</item>
/// <item>A well-connected entry decaying FASTER than an isolated one, over a REPLAYED corpus.</item>
/// <item><see cref="IMemoryRetrievabilityPolicy.CandidateCutoff"/> failing its superset property over REAL
/// replayed states, not a synthetic grid.</item>
/// <item>Reinforcement that never fires across a realistic session — the exact pathology
/// (<c>docs/task-archive.md</c> Part 55) that made a PREDECESSOR sweep meaningless.</item>
/// <item><b>Own probe</b>: DSR stays internally correct — contract-compliant, never a broken probability —
/// under a reuse pattern that starves its reinforcement.</item>
/// </list>
/// <para><b>Every item runs against whatever curve(s) <see cref="Curves"/> yields</b> — today just
/// <see cref="DsrRetrievability"/>, but the shape is deliberately kept extensible: a future curve variant
/// (for instance a difficulty-live vs. difficulty-inert DSR pairing) adds a row rather than a new file.</para>
/// <para><b>SQLite by default</b>: the replayed-corpus items depend on which entries a recall RANKS into its
/// top ten, and the in-process store reports a flat relevance of <c>1</c> for every match, so only the
/// full-text path's bm25 separates them.</para>
/// </summary>
public class DsrPathologyTests
{
    private const string EngineName = "falsify";
    private const int Seed = 12345;

    /// <summary>An undamped per-write age policy, matching the substitution
    /// <see cref="MemoryDecaySimulationTests"/>, <c>MemoryDefaultRecallQualityTests</c> and the historical
    /// sweep all make, for the same reason: a fast in-process replay lands entirely inside
    /// <see cref="BurstDampenedAgePolicy"/>'s wall-clock burst window and would measure the damping instead of
    /// the curve under test.</summary>
    private static GraphMemoryEngine BuildEngine(IMemoryGraphStore store, IMemoryRetrievabilityPolicy policy,
        IMemoryRankingPolicy? ranking = null) =>
        new(EngineName, store, seams: new GraphMemorySeams
            {
                Retrievability = policy,
                AgePolicies = [new PerWriteAgePolicy()],
                Ranking = ranking,
            });

    /// <summary>The curve(s) every fact in this file runs against.
    /// <para><b>Reinforcement is switched ON here, and that is deliberate as of 3.0.</b>
    /// <c>DsrOptions.ReinforceGain</c> now defaults to <c>0</c> (<c>docs/DECISIONS.md</c> D54), so a
    /// default-constructed curve never grows a stability at all — and every pathology in this file is about
    /// what the growth arithmetic does under an adversarial pattern. Run against the bare default they would
    /// all pass trivially, for the same reason a calculator that returns zero never overflows: the subject
    /// would have been removed rather than tested.</para>
    /// <para>Pathologies of the 3.0 DEFAULT configuration are covered elsewhere and deliberately not
    /// duplicated here — <c>MemoryDefaultRecallQualityTests</c> pins its recall quality end to end, and
    /// <c>DsrRetrievabilityTests.Reinforcement_is_OFF_by_default_as_of_3_0</c> pins the default itself.</para></summary>
    public static IEnumerable<object[]> Curves()
    {
        yield return ["Dsr", (Func<IMemoryRetrievabilityPolicy>)(() =>
            new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 }))];
    }

    // ---------------------------------------------------------------------------------------------------
    // 1. A memory becoming permanently unreachable.
    // ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Curves))]
    public async Task A_written_entry_is_never_permanently_unreachable_after_a_realistic_session(
        string label, Func<IMemoryRetrievabilityPolicy> factory)
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = BuildEngine(store, factory());

        const int targets = 12;
        for (var i = 0; i < targets; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"item target{i} is a fact mentioned exactly once"));

        // a realistic session that never revisits the targets again — the worst case for burial, and the
        // only condition under which "buried, not cut" is actually being tested rather than assumed
        for (var round = 0; round < 20; round++)
            for (var i = 0; i < 10; i++)
                await engine.RememberAsync(new MemoryWrite("t", "s", $"item noise{round}x{i} was mentioned once"));

        for (var i = 0; i < targets; i++)
        {
            var targeted = await engine.RecallAsync(new MemoryQuery("t", "s", $"target{i}", Limit: 5));
            Assert.True(targeted.Items.Any(x => x.Headline.Contains($"target{i}", StringComparison.Ordinal)),
                $"[{label}] target{i} is UNREACHABLE by a direct, targeted query after 200 unrelated writes " +
                "aged it — burial is acceptable, this is not.");
        }
    }

    // ---------------------------------------------------------------------------------------------------
    // 2. Stability collapsing or exploding under a reachable sequence of writes and recalls.
    // ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Curves))]
    public async Task Stability_never_collapses_or_explodes_across_a_long_reuse_session(
        string label, Func<IMemoryRetrievabilityPolicy> factory)
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var policy = factory();
        var engine = BuildEngine(store, policy);

        var reference = (await engine.RememberAsync(
            new MemoryWrite("t", "s", "item reused0 is heavily reused material"))).Reference;

        // 100 reuse touches, one filler write interposed each time — ten times CorpusShape's own widest
        // named ReuseRatio (10, "high-reuse"), a deliberate stress case for compounding.
        for (var i = 0; i < 100; i++)
        {
            await engine.RecallAsync(new MemoryQuery("t", "s", "reused0"));
            await engine.RememberAsync(new MemoryWrite("t", "s", $"item filler{i} was written only to interpose age"));
        }

        var node = await store.GetAsync(EngineName, long.Parse(reference.Id, CultureInfo.InvariantCulture));
        Assert.NotNull(node);
        // a guard that cannot observe the thing it guards is worse than none (.claude/knowledge/pitfalls.md):
        // if the recall above never actually reinforced this entry (e.g. outranked by fresher filler, the
        // same corpus/ranking interaction item 5 below measures), stability would sit frozen at
        // InitialStability and the collapse/explosion assertions below would pass VACUOUSLY on a session
        // where reinforcement never fired at all — the opposite of what "across a long reuse session"
        // claims to have exercised. Every neighbouring fact in this file has a check like this.
        Assert.True(node!.Stability > policy.InitialStability,
            $"[{label}] stability never grew past its own InitialStability {policy.InitialStability} " +
            $"({node.Stability}) after 100 reuse touches — reinforcement did not fire, so the collapse/" +
            "explosion assertions below would pass vacuously; this test would be checking nothing.");
        Assert.True(double.IsFinite(node.Stability),
            $"[{label}] stability is not finite after 100 reinforcements: {node.Stability}");
        Assert.True(node.Stability >= policy.InitialStability - 1e-9,
            $"[{label}] stability COLLAPSED below its own initial value {policy.InitialStability}: {node.Stability}");
        // DsrOptions.MaxStability's default, hardcoded deliberately so a change to the default breaks this
        // assertion rather than silently tracking it.
        Assert.True(node.Stability <= 2000 + 1e-6,
            $"[{label}] stability EXPLODED past the documented ceiling of 2000: {node.Stability}");
    }

    /// <summary>Lowering <c>MaxStability</c> under an existing corpus FREEZES an entry already above it: the
    /// ceiling caps GROWTH and never CUTS (<c>docs/task-archive.md</c> Part 54, DSR2). Reachable by
    /// reconfiguration alone, which is why it belongs in item 2.
    /// <para>An ENGINE + SQLite round trip rather than a direct call, because the engine PERSISTS
    /// <c>Reinforce</c>'s return, which is what would make a cut permanent. The equality is two-sided:
    /// growth is on and the entry is aged, so a bare <c>Math.Min</c> CUTS it to 2000 and a missing
    /// <c>Math.Min</c> lets it COMPOUND past 3000.</para></summary>
    [Fact]
    public async Task Lowering_MaxStability_under_an_existing_corpus_FREEZES_a_memory_rather_than_shortening_it()
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);

        // seed a stability already past a ceiling we are ABOUT to configure lower than it
        var write = new GraphNodeWrite(EngineName, "t", "s", "h", "item ceiling0 already durable",
            MemoryGrade.Associative, InitialStability: 3000, Advance: 1, Metadata: null);
        var id = await store.UpsertAsync(write);

        var lowCeiling = new DsrRetrievability(new DsrOptions { MaxStability = 2000, ReinforceGain = 2.0 });
        var engine = BuildEngine(store, lowCeiling);
        // aged, so law 3's spacing term is non-zero and an uncapped reinforcement would really grow it
        for (var i = 0; i < 20; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"item filler{i} was written only to interpose age"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "ceiling0"));

        var node = await store.GetAsync(EngineName, id);
        Assert.NotNull(node);
        // the provenance bit proves Reinforce ran: the row was written with NONE, and only a touch sets it
        Assert.Contains(recalled.Items, i => i.Headline == "h");
        Assert.Equal((long)MemoryRetrievabilityProvenance.Dsr, node!.ProvenanceRetrievability);

        Assert.Equal(3000, node.Stability, precision: 6);
    }

    // ---------------------------------------------------------------------------------------------------
    // 3. A well-connected entry decaying faster than an isolated one, over a replayed corpus.
    // ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Curves))]
    public async Task A_well_connected_entry_never_decays_faster_than_an_isolated_one_across_a_replayed_session(
        string label, Func<IMemoryRetrievabilityPolicy> factory)
    {
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var policy = factory();
        var engine = BuildEngine(store, policy);

        var connectedRef = (await engine.RememberAsync(
            new MemoryWrite("t", "s", "item connected0 shares context with its neighbours"))).Reference;
        var isolatedRef = (await engine.RememberAsync(
            new MemoryWrite("t", "s", "item isolated0 stands entirely alone"))).Reference;
        var hub1 = (await engine.RememberAsync(
            new MemoryWrite("t", "s", "item hub1 co-occurs with connected material"))).Reference;
        var hub2 = (await engine.RememberAsync(
            new MemoryWrite("t", "s", "item hub2 co-occurs with connected material"))).Reference;

        // real connectivity through the PUBLIC API — never a hand-edited store
        await engine.LinkAsync(connectedRef, hub1, weight: 5);
        await engine.LinkAsync(connectedRef, hub2, weight: 5);

        // both entries age through the SAME subsequent writes — connectivity is the only difference
        for (var i = 0; i < 150; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"item filler{i} was written only to interpose age"));

        var connectedNode = await store.GetAsync(EngineName, long.Parse(connectedRef.Id, CultureInfo.InvariantCulture));
        var isolatedNode = await store.GetAsync(EngineName, long.Parse(isolatedRef.Id, CultureInfo.InvariantCulture));
        Assert.NotNull(connectedNode);
        Assert.NotNull(isolatedNode);

        // a guard that cannot observe the thing it guards is worse than none (.claude/knowledge/pitfalls.md)
        Assert.True(connectedNode!.Strength > 0,
            $"[{label}] the connectivity setup did not register (Strength={connectedNode.Strength}) — this " +
            "test would pass vacuously without this check.");

        // connected0 was written BEFORE isolated0, so it is inherently one position OLDER — two distinct
        // writes cannot share a position, and that is a write-order artifact, not the property under test.
        // Sanity-bound it (a wildly different age would mean something else is wrong), then EQUALIZE age
        // before comparing retrievability — the strength/connectivity signal under test is still 100% real,
        // taken from a REPLAYED session; only the incidental age offset from write order is normalized out.
        Assert.True(Math.Abs(connectedNode.Age - isolatedNode!.Age) <= 1,
            $"[{label}] connected0 and isolated0 were written back-to-back but their ages differ by more " +
            $"than the expected one-write offset: connected={connectedNode.Age}, isolated={isolatedNode.Age}.");
        var equalAge = Math.Min(connectedNode.Age, isolatedNode.Age);

        var connectedR = policy.Retrievability(connectedNode.DecayState with { Age = equalAge });
        var isolatedR = policy.Retrievability(isolatedNode.DecayState with { Age = equalAge });

        Assert.True(connectedR >= isolatedR - 1e-12,
            $"[{label}] a well-connected entry (r={connectedR:F6}, strength={connectedNode.Strength:F2}) " +
            $"decayed FASTER than an isolated one (r={isolatedR:F6}) over a REPLAYED session at equal age " +
            $"{equalAge:F0} — connectedness must never lower retrievability.");
    }

    // ---------------------------------------------------------------------------------------------------
    // 4. CandidateCutoff's superset property, over real replayed states rather than a synthetic grid.
    // ---------------------------------------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(Curves))]
    public async Task CandidateCutoff_remains_a_conservative_superset_over_real_replayed_states(
        string label, Func<IMemoryRetrievabilityPolicy> factory)
    {
        var policy = factory();
        const double floor = 0.05; // GraphMemoryOptions.MinRetrievability's own shipped default

        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = BuildEngine(store, policy);

        var corpus = MemoryCorpus.Generate(CorpusShape.Default, Seed);
        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;
        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    await engine.RememberAsync(w.Write);
                    break;
                case CorpusQuery q:
                    await engine.RecallAsync(new MemoryQuery(firstWrite.TaskKey, firstWrite.Scope, q.Text, Limit: 10));
                    break;
            }
        }

        var cutoff = policy.CandidateCutoff(floor);
        var allNodes = await store.SeedAsync(EngineName, firstWrite.TaskKey, firstWrite.Scope, query: null, limit: int.MaxValue);

        var evaluated = 0;
        var violations = new List<string>();
        foreach (var node in allNodes)
        {
            if (node.Grade == MemoryGrade.Authoritative) continue; // exempt — retrievability fixed at 1
            if (node.Stability <= 0) continue; // defensive; should not occur with these defaults
            var r = policy.Retrievability(node.DecayState);
            evaluated++;
            if (r < floor) continue; // may legitimately be excluded; only the keepers matter

            var ratio = node.Age / node.Stability;
            if (ratio > cutoff)
                violations.Add($"node {node.Id} ({node.Headline}): r={r:F6} >= floor {floor}, but " +
                    $"age/stability={ratio:F3} > cutoff {cutoff:F3}");
        }

        Assert.True(evaluated > 50,
            $"[{label}] the replay produced too few evaluable nodes ({evaluated}) for this check to mean anything.");
        Assert.True(violations.Count == 0,
            $"[{label}] CandidateCutoff({floor}) = {cutoff:F3} is NOT a conservative superset over " +
            $"{evaluated} REAL replayed states — {violations.Count} violation(s), e.g.: {violations.FirstOrDefault()}");
    }

    // ---------------------------------------------------------------------------------------------------
    // 5. Reinforcement that never fires across a realistic session — the r=1-always pathology, checked as
    //    a property of THIS corpus rather than assumed fixed by an earlier retarget.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// Among ids the engine actually RETURNED for one of their own relevant queries, no stability is left
    /// frozen at <see cref="IMemoryRetrievabilityPolicy.InitialStability"/> — the r=1-always pathology of
    /// <c>docs/task-archive.md</c> Part 55.
    /// <para><b>"Declared relevant" and "recalled" are not the same set.</b> In <see cref="CorpusShape.Default"/>
    /// a handful of relevant ids (mostly early <c>topic*</c>/<c>hot*</c> entries) are never recalled at all,
    /// outranked by the fresh <c>WriteFiller</c> padding <c>MemoryCorpus</c> emits to reach the discriminating
    /// age band, so <see cref="IMemoryRetrievabilityPolicy.Reinforce"/> is never called for them. That is a
    /// corpus/ranking interaction rather than a reinforcement defect, so it is bounded separately below.</para>
    /// </summary>
    [Theory]
    [MemberData(nameof(Curves))]
    public async Task Reinforcement_is_not_a_total_no_op_whenever_the_entry_is_actually_recalled(
        string label, Func<IMemoryRetrievabilityPolicy> factory)
    {
        var policy = factory();
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = BuildEngine(store, policy);

        var corpus = MemoryCorpus.Generate(CorpusShape.Default, Seed);
        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;

        var corpusIdToRef = new Dictionary<string, string>(StringComparer.Ordinal);
        var everRelevant = new HashSet<string>(StringComparer.Ordinal);
        var everRecalled = new HashSet<string>(StringComparer.Ordinal);

        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = (await engine.RememberAsync(w.Write)).Reference;
                    corpusIdToRef[MemoryCorpusTestAccess.IdOf(w.Write.Content)] = memRef.Id;
                    break;
                case CorpusQuery q:
                    var recall = await engine.RecallAsync(
                        new MemoryQuery(firstWrite.TaskKey, firstWrite.Scope, q.Text, Limit: 10));
                    var recalledCorpusIds = recall.Items
                        .Select(i => i.Headline.Split(' ', 3) is [_, var cid, ..] ? cid : i.Reference.Id)
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var id in q.RelevantIds)
                    {
                        everRelevant.Add(id);
                        if (recalledCorpusIds.Contains(id)) everRecalled.Add(id);
                    }
                    break;
            }
        }

        Assert.True(everRelevant.Count > 10,
            $"[{label}] too few relevant ids ({everRelevant.Count}) in this corpus for the check to mean anything.");

        // --- the DISQUALIFYING check, correctly scoped: among ids the engine actually recalled for one of
        // their own relevant queries, is any stability left frozen at InitialStability regardless? ---
        var recalledButFrozen = new List<string>();
        foreach (var corpusId in everRecalled)
        {
            if (!corpusIdToRef.TryGetValue(corpusId, out var refId)) continue;
            var node = await store.GetAsync(EngineName, long.Parse(refId, CultureInfo.InvariantCulture));
            if (node is null || node.Grade == MemoryGrade.Authoritative) continue;
            if (node.Stability <= policy.InitialStability * 1.0001)
                recalledButFrozen.Add($"{corpusId}: stability={node.Stability:F4}");
        }

        Assert.True(recalledButFrozen.Count == 0,
            $"[{label}] {recalledButFrozen.Count}/{everRecalled.Count} entries that WERE actually recalled " +
            "for one of their own relevant queries never grew past InitialStability — this is the r=1-always " +
            $"pathology that made a predecessor sweep meaningless (docs/task-archive.md Part 55). Examples: " +
            $"{string.Join("; ", recalledButFrozen.Take(5))}");

        // --- the SEPARATE, shared finding: a real, non-trivial fraction of relevant ids are never recalled
        // for ANY of their own relevant queries at all — swallowed by fresh WriteFiller padding, not a
        // reinforcement-formula issue. Asserted here only as "comparable across curves" (a subsystem
        // property), never as "zero" (it is not, and asserting otherwise would be false).
        var neverRecalledCount = everRelevant.Count - everRecalled.Count;
        Console.WriteLine($"[{label}] never-recalled-for-any-own-query count: {neverRecalledCount}/{everRelevant.Count}");
        Assert.True(neverRecalledCount < everRelevant.Count / 2,
            $"[{label}] {neverRecalledCount}/{everRelevant.Count} relevant ids were NEVER recalled for any " +
            "of their own relevant queries — if this ever became a MAJORITY of the corpus, the corpus would " +
            "be measuring filler-competition noise rather than the forgetting curve.");
    }

    // ---------------------------------------------------------------------------------------------------
    // 6. Own probe: DSR's internal correctness under a reuse pattern that starves its reinforcement.
    // ---------------------------------------------------------------------------------------------------

    /// <summary>Distinguishes "DSR correctly declines to over-strengthen a freshly-recalled entry" from "DSR
    /// is wrong". The pattern — a write aged past the discriminating band's own ceiling, then repeat queries
    /// with exactly ONE filler write between them, never zero — starves reinforcement after the first touch,
    /// and DSR's own contract is re-checked on the REAL per-touch trace it produces: reinforcement never
    /// shortens, retrievability stays a probability. Starvation alone is a ranking-competition effect of what
    /// <see cref="MultiplicativeRankingPolicy"/> rewards; a failure HERE would be a DSR defect.
    /// <para>Every recall is asserted to RETURN the target, and the target's stability to have grown past
    /// its initial value by the end: without both, "stability never shrinks" holds for an entry nothing ever
    /// reinforced.</para></summary>
    [Theory]
    [MemberData(nameof(Curves))]
    public async Task Own_probe_Dsr_stays_internally_correct_under_the_exact_pattern_that_starves_it(
        string label, Func<IMemoryRetrievabilityPolicy> factory)
    {
        var policy = factory();
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = BuildEngine(store, policy);

        var reference = (await engine.RememberAsync(
            new MemoryWrite("t", "s", "item target0 covers ordinary material queried on its own terms"))).Reference;
        for (var i = 0; i < 100; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"item filler{i} was written only to interpose age"));

        double? previousStability = null;
        for (var k = 0; k < 4; k++)
        {
            if (k > 0)
                await engine.RememberAsync(
                    new MemoryWrite("t", "s", $"item betweenfiller{k} was written only to interpose age"));
            var recall = await engine.RecallAsync(new MemoryQuery("t", "s", $"target0 repeat{k}", Limit: 10));
            Assert.Contains(recall.Items, i => i.Reference.Id == reference.Id);
            var node = await store.GetAsync(EngineName, long.Parse(reference.Id, CultureInfo.InvariantCulture));
            Assert.NotNull(node);

            if (previousStability is double prev)
                Assert.True(node!.Stability >= prev - 1e-9,
                    $"[{label}] touch {k}: stability SHRANK from {prev:F4} to {node.Stability:F4} — violates " +
                    "IMemoryRetrievabilityPolicy.Reinforce's own documented contract " +
                    "(Reinforcement_never_shortens_a_memory).");
            previousStability = node!.Stability;

            var r = policy.Retrievability(node.DecayState);
            Assert.InRange(r, 0, 1);
        }

        Assert.True(previousStability > policy.InitialStability,
            $"[{label}] the target never grew past InitialStability ({previousStability}), so the " +
            "never-shrinks check above had nothing to observe");
    }

    /// <summary><b>A non-finite <see cref="MemoryDecayState.Age"/> must not reach
    /// <see cref="MemoryDecayState.Difficulty"/>.</b> <c>Age</c> arrives per-call in the caller's own state, so
    /// it cannot be validated at construction the way <see cref="DsrOptions"/> validates its constants, and
    /// <c>Math.Clamp</c> propagates <c>NaN</c> (<c>.claude/knowledge/pitfalls.md</c>) — so an unguarded
    /// <c>NaN</c> age becomes a <c>NaN</c> grade, difficulty and review-log row. The guard reports NO judgement:
    /// <c>null</c>, the meaning the Δt=0 bypass already carries.
    /// <para><b>The three non-finite ages are not one case.</b> <c>NaN</c> survives <c>state.Age &lt;= 0</c>
    /// and <c>Math.Clamp</c>, so it is the one that reaches the grade and the only one asserted <c>null</c>.
    /// <c>-Infinity</c> is caught by the Δt=0 branch. <c>+Infinity</c> is COMPUTABLE —
    /// <c>Math.Pow(+Infinity, decay)</c> with a negative exponent is exactly <c>0</c>, "fully forgotten" — and
    /// derives a real Hard grade.</para></summary>
    [Fact]
    public void A_non_finite_age_never_produces_a_non_finite_grade_or_difficulty()
    {
        var policy = new DsrRetrievability();

        foreach (var age in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var state = new MemoryDecayState(age, RecallCount: 1, Stability: 20);

            var grade = policy.DerivedGrade(state);
            Assert.True(grade is null || double.IsFinite(grade.Value),
                $"age {age}: DerivedGrade returned {grade}");

            var reinforced = policy.Reinforce(state);
            Assert.True(double.IsFinite(reinforced.Difficulty),
                $"age {age}: Reinforce returned Difficulty {reinforced.Difficulty}");
            Assert.InRange(reinforced.Difficulty, 1, 10);
            Assert.True(double.IsFinite(reinforced.Stability),
                $"age {age}: Reinforce returned Stability {reinforced.Stability}");
        }

        // NaN specifically: no judgement at all, because nothing computable happened
        Assert.Null(policy.DerivedGrade(new MemoryDecayState(double.NaN, RecallCount: 1, Stability: 20)));
    }

    /// <summary><b>…and the engine path that actually reaches it, which is NOT the recall path.</b>
    /// <see cref="GraphMemoryEngine.RecallAsync"/> is protected by accident — <c>MemoryRankingContract.Rankable</c>
    /// drops a candidate whose retrievability is non-finite, so the poisoned entry never reaches
    /// reinforcement. <see cref="GraphMemoryEngine.ExpandAsync"/> reinforces a single node the caller named,
    /// with no ranking in between, so nothing filters it.
    /// <para><b>That is the endorsed path, which is what makes this worth a fact rather than a note.</b>
    /// <c>docs/memory.md</c> recommends
    /// <c>ReinforceOn = MemoryReinforcementActs.Expansion</c> as the measurably better setting, so the
    /// unguarded route is the one a consumer following the documentation takes.</para>
    /// <para><b>InMemory deliberately, not SQLite.</b> On a SQL backend the poisoned write throws and
    /// <c>ReinforceAsync</c>'s catch-all swallows it, so the damage is a silently-lost reinforcement and the
    /// stored value stays finite — this fact would pass while the defect was live. The in-process store
    /// persists the <c>NaN</c>, which is what makes it observable.</para></summary>
    [Fact]
    public async Task Expanding_under_a_non_finite_age_policy_never_persists_a_non_finite_difficulty()
    {
        var store = new Lyntai.Storage.InMemory.InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("project/graph", store, seams: new GraphMemorySeams
            {
                AgePolicies = [new NonFiniteAgePolicy()],
            });

        var reference = (await engine.RememberAsync(new MemoryWrite("t", "s", "gadget ordinary one"))).Reference;
        await engine.ExpandAsync(reference);

        var node = Assert.Single(await store.SeedAsync("project/graph", "t", "s", null, 10, CancellationToken.None));
        Assert.True(double.IsFinite(node.DecayState.Difficulty), $"persisted Difficulty is {node.DecayState.Difficulty}");
    }

    /// <summary>A BYO age policy reporting a non-finite age — the public seam that makes the two facts above
    /// reachable. <see cref="IMemoryAgePolicy.Age"/> is an interface member on somebody else's
    /// implementation, so unlike <see cref="DsrOptions"/>' own constants there is nowhere to validate it.</summary>
    private sealed class NonFiniteAgePolicy : IMemoryAgePolicy
    {
        public MemoryAgeKind Kind => MemoryAgeKind.Derivable;
        public MemoryTick Advance(MemoryWrite write, string engine) => MemoryTick.One;
        public double Age(MemoryAgeSample sample) => double.NaN;
    }
}
