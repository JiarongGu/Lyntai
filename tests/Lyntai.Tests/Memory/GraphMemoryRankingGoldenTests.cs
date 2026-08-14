using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>
/// CHARACTERIZATION, written before the ranking seam existed (the memory-ranking-seam plan). Each golden
/// fact's only job is to fail loudly if extracting <see cref="GraphMemoryEngine.RecallAsync"/>'s rank formula
/// — <c>Relevance × Retrievability × boost × HopAttenuation^hop</c>, then a relative floor — into a swappable
/// <see cref="IMemoryRankingPolicy"/> changed what comes back for an unchanged corpus. Every fact asserts an
/// exact ORDER, not a property — a property test would still pass under a subtly different formula, which is
/// the whole risk a refactor like this carries. Now that the seam exists (<c>MultiplicativeRankingPolicy</c>
/// carries the formula), every fact below passes an explicit <see cref="MultiplicativeRankingPolicy"/> to the
/// engine's own <c>ranking</c> parameter rather than relying on whichever policy happens to be a bare-
/// constructed engine's own default — the assertions and expected orders are UNCHANGED, only where the knob
/// lives moved. <b>Two facts needed this from the start (the seam extraction, when it still agreed with the
/// bare default); three more needed it added 2026-08-11</b>, the day <see cref="Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy"/>
/// became the registered AND bare-constructor default (owner ruling — this library's own measurement found
/// it beating this policy on the corpus's `topical` class): each of those three facts characterizes a
/// MULTIPLICATIVE-NAMED term (<c>HopAttenuation</c>, a product of relevance and retrievability, retrievability's
/// "own multiplicative contribution") that has no RRF analogue, so leaving them implicit would have silently
/// repointed what they test at RRF's own formula instead — see each fact's own remarks for why passing the
/// policy explicitly, not re-baselining to new numbers, is the correct fix for a test whose SUBJECT is a
/// named formula rather than "whatever today's default is" (that subject is
/// <c>MemoryDefaultRecallQualityTests</c>'s own job, and IT re-baselined for real).
/// <para><b>Five facts, one per factor that needs to survive the refactor untouched</b> — a golden test that
/// exercises only some of a formula's terms would let a rewrite silently drop or mis-wire the others and
/// still pass (<c>.claude/knowledge/pitfalls.md</c>, "Testing"). Each fact's corpus is built so the term it
/// pins is DOING WORK — proved, not assumed, by temporarily perturbing that one term, observing the order
/// move (and the fact fail), then reverting:</para>
/// <list type="bullet">
/// <item><see cref="Recall_order_over_a_fixed_corpus_is_what_it_is_today"/> pins <c>HopAttenuation</c> — a
/// direct hit, a hop-1 and a hop-2 entry, the seed aged until it is the weakest direct hit. Measured:
/// <c>HopAttenuation</c> 0.5 → 0.9 turned <c>[wrap-up, seed, hop1, hop2]</c> into
/// <c>[wrap-up, hop1, hop2, seed]</c>.</item>
/// <item><see cref="Recall_order_is_pinned_when_salience_reorders_a_recall"/> pins the salience
/// <c>boost</c> term — <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/> set high enough that an
/// OLDER, salient entry outranks a fresher, neutral one that pure recency would otherwise put first.
/// Measured: dropping the weight back to 0 (the shipped default) reverted the order to pure recency.</item>
/// <item><see cref="Recall_order_over_a_fixed_corpus_buries_what_falls_below_the_floor"/> pins
/// <see cref="MultiplicativeRankingOptions.RelativeFloor"/> — burial itself, not just ordering: the weakest
/// entry is absent from the result, not merely last. Measured: dropping the floor to 0 brought it back.</item>
/// <item><see cref="Recall_order_over_a_fixed_corpus_needs_relevance_and_retrievability_multiplied"/> pins
/// <see cref="GraphNode.Relevance"/> against <see cref="SqliteMemoryGraphStore"/>, where it is a real rank
/// POSITION rather than <see cref="InMemoryMemoryGraphStore"/>'s flat 1 — two candidates whose bm25 relevance
/// order and recency order point opposite ways, so the asserted order is only right if both are multiplied.
/// Measured: replacing <c>Relevance</c> with the constant <c>1</c> in the engine's own formula flipped the
/// order.</item>
/// <item><see cref="Recall_order_over_a_fixed_corpus_is_decided_by_retrievability_alone"/> pins
/// <c>Retrievability</c>'s own multiplicative contribution, isolated from the other three terms — same
/// salience, same hop, and Relevance tied at <see cref="InMemoryMemoryGraphStore"/>'s flat 1 for both
/// candidates. The corpus deliberately does NOT coincide with the id-descending tiebreak, so if
/// Retrievability's contribution were ever dropped the tiebreak alone would pick the WRONG entry.</item>
/// </list>
/// <para><b>A sixth fact, added when the engine started calling the policy</b>:
/// <see cref="An_authoritative_entry_the_policy_buried_is_still_returned"/> is not a characterization of the
/// FORMULA — it pins the exemption the ENGINE, not the policy, owns: trust that authoritative material is
/// never buried must hold whatever policy is installed, including a hostile one that drops everything.</para>
/// <para><b>A seventh and eighth fact</b>:
/// <see cref="Authoritative_entries_take_reserved_slots_and_displace_ordinary_material"/> and
/// <see cref="The_authoritative_reserve_bounds_how_much_ordinary_material_is_displaced"/>.
/// <b>The seventh asserted the OPPOSITE until 2026-08-13</b> — that a re-admitted entry was appended after
/// the policy's order and could still be cut by the limit. That claim lived in four places (the engine's XML
/// doc, design §5.7, README, CHANGELOG) and this was the only one of the four that was also a test, which is
/// exactly why it is worth recording that the test was pinning a defect: design §5.7.0's objective (1) has
/// NO acceptable failure rate, and the first end-to-end measurement found every authoritative fact lost in
/// every language. The eighth pins the bound that answers the original objection.</para>
/// </summary>
public sealed class GraphMemoryRankingGoldenTests
{
    /// <summary>An undamped per-write age policy and NO retention policies, so nothing salience-derived can
    /// perturb the baseline — mirrors <see cref="GraphMemoryEngineTests"/>'s own helper. Passing no
    /// <c>policy</c> takes the plain <c>DsrRetrievability</c> straight (the bare constructor's own default
    /// since <c>HalfLifeRetrievability</c> was deleted — <c>docs/DECISIONS.md</c>), never wrapped in a
    /// <c>ModulatedRetrievability</c> with any retention policy registered.
    /// <para><b>Ranking passed EXPLICITLY, deliberately not left at the bare constructor's own default —
    /// corrected 2026-08-11.</b> <see cref="ReciprocalRankFusionPolicy"/> became that default (owner ruling)
    /// the same day <see cref="MultiplicativeRankingPolicy"/> stopped being the DI-registered one, but the
    /// ONE fact this helper builds for (<see cref="Recall_order_over_a_fixed_corpus_is_what_it_is_today"/>)
    /// characterizes <see cref="MultiplicativeRankingOptions.HopAttenuation"/> BY NAME — a Multiplicative-
    /// specific term with no RRF analogue — so letting it silently follow whichever policy the bare
    /// constructor defaults to today would test something the fact was never about. Passing
    /// <see cref="MultiplicativeRankingPolicy"/> explicitly here is the SAME judgement call the class doc's
    /// own opening paragraph already made for the salience and floor facts when the ranking seam was
    /// extracted ("the two facts... instead pass an explicit MultiplicativeRankingPolicy... only where the
    /// knob lives moved") — applied to the one remaining implicit fact, for the identical reason: what a
    /// characterization test is FOR should not drift just because a DEFAULT moved.</para></summary>
    private static GraphMemoryEngine BuildEngine() =>
        new("project/graph", new InMemoryMemoryGraphStore(), agePolicies: [new PerWriteAgePolicy()],
            ranking: new MultiplicativeRankingPolicy());

    /// <summary>A second (or third) view of the SAME store and engine name, differing only in the
    /// salience policy bound to it — the device <c>GraphMemoryRankingTests</c> already uses to give distinct
    /// writes distinct salience, since the salience policy is per-engine-instance rather than per-write. Still
    /// no retention policy: <c>SalienceRankWeight</c> is a RANK term read straight off the stored signals bag,
    /// not a retrievability modulation.</summary>
    private static GraphMemoryEngine EngineWithSaliencePolicy(IMemoryGraphStore store, IMemorySaliencePolicy saliencePolicy,
        IMemoryRankingPolicy? ranking = null) =>
        new("project/graph", store, agePolicies: [new PerWriteAgePolicy()], saliencePolicies: [saliencePolicy], ranking: ranking);

    /// <summary>Reports a fixed salience so a fact pins the RANK plumbing rather than the default
    /// salience policy's curve — mirrors <c>GraphMemoryRankingTests.FixedSaliencePolicy</c>.</summary>
    private sealed class FixedSaliencePolicy(double salience) : IMemorySaliencePolicy
    {
        // a fake's own bit, from the consumer range (32-62) — never None: fix round 2's provenance
        // validation rejects a policy declaring None, since every REAL, running policy has an identity.
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context) =>
            MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, salience);
    }

    private static Task<MemoryRef> Remember(GraphMemoryEngine engine, string content) =>
        engine.RememberAsync(new MemoryWrite("t", "s", content));

    /// <summary>Ages whatever is already stored by writing unrelated material that matches neither the
    /// query nor any link — the same aging device <see cref="GraphMemoryEngineTests"/> uses.</summary>
    private static async Task Crowd(GraphMemoryEngine engine, int writes)
    {
        for (var i = 0; i < writes; i++) await Remember(engine, $"unrelated filler note {i}");
    }

    [Fact]
    public async Task Recall_order_over_a_fixed_corpus_is_what_it_is_today()
    {
        // CHARACTERIZATION, written before the ranking seam exists. Its only job is to fail loudly if
        // extracting the formula changes what comes back. It asserts an exact ORDER, not a property —
        // a property test would pass under a subtly different formula, which is the whole risk here.
        var engine = BuildEngine();

        var seed = await Remember(engine, "alpha migration rollout begins across the fleet");
        await Crowd(engine, 15); // ages the seed until it is the WEAKEST direct hit, not the strongest
        var hop1 = await Remember(engine, "on-call rotation notes mention the fleet rollout");
        var hop2 = await Remember(engine, "escalation contact list attached to the on-call rotation");
        await Remember(engine, "alpha migration wraps up with a retro");

        await engine.LinkAsync(seed, hop1, symmetric: true);
        await engine.LinkAsync(hop1, hop2, symmetric: true);

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "alpha", Limit: 5));

        Assert.Equal(
            [
                "alpha migration wraps up with a retro",
                "alpha migration rollout begins across the fleet",
                "on-call rotation notes mention the fleet rollout",
                "escalation contact list attached to the on-call rotation",
            ],
            recalled.Items.Select(i => i.Headline).ToArray());
    }

    [Fact]
    public async Task Recall_order_is_pinned_when_salience_reorders_a_recall()
    {
        // FACT A: pins MultiplicativeRankingOptions.SalienceRankWeight's contribution to Rank's `boost`
        // term — moved off GraphMemoryOptions in the wiring task; the engine now takes an explicit
        // IMemoryRankingPolicy instead of reading the weight off its own options. Without it, pure recency
        // would put the freshly-written "beta" on top — it is younger, so its retrievability alone already
        // exceeds "alpha"'s. Weight 1.0 and salience 4 (the salience policy's own ceiling — see
        // MultiplicativeRankingOptions.SalienceRankWeight's doc) on "alpha" is chosen to clear that gap
        // (boost 1 + ln(4) ≈ 2.386 against a retrievability ratio close to 1 at only 6 writes of age), the
        // same margin GraphMemoryRankingTests's opt-in fact already establishes against a harder,
        // rank-position-normalized store — only possible through the boost term, never through recency
        // alone.
        var store = new InMemoryMemoryGraphStore();
        var ranking = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions { SalienceRankWeight = 1.0 });
        var salient = EngineWithSaliencePolicy(store, new FixedSaliencePolicy(4), ranking);
        var ordinary = EngineWithSaliencePolicy(store, new FixedSaliencePolicy(1), ranking);

        await Remember(salient, "widget beacon alpha"); // older, salient
        await Crowd(salient, 5);
        await Remember(ordinary, "widget beacon beta"); // freshest, neutral salience

        var recalled = await salient.RecallAsync(new MemoryQuery("t", "s", "widget", Limit: 5));

        Assert.Equal(
            ["widget beacon alpha", "widget beacon beta"],
            recalled.Items.Select(i => i.Headline).ToArray());
    }

    [Fact]
    public async Task Recall_order_over_a_fixed_corpus_buries_what_falls_below_the_floor()
    {
        // FACT B: pins MultiplicativeRankingOptions.RelativeFloor — BURIAL, not just ordering, now moved off
        // GraphMemoryOptions. The default (0.02) is nowhere near strict enough to exclude anything in a
        // corpus this size, so this fact pins a floor high enough that the weakest entry is actually cut
        // from the result while the three survivors keep their relative order — the only way the later
        // refactor's floor handling gets checked at all.
        var ranking = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions { RelativeFloor = 0.1 });
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            agePolicies: [new PerWriteAgePolicy()], ranking: ranking);

        await Remember(engine, "floor probe delta fades far in the back"); // ages past the floor
        // 2000, not 100 (2026-08-10, fsrs-properly plan Task 1): the deleted exponential curve fell under
        // this 0.1 relative floor at a crowd of 100 (measured then: r≈0.24, still above it, at a crowd of
        // 100). DsrRetrievability's heavier tail — the reason it was adopted — needs far more age to fall
        // under the same floor. MEASURED (fix round 1): a crowd of 1000 reached only 0.081057 — technically
        // under 0.1, but with a margin (≈19%) tighter than every other retuned threshold in this sweep;
        // 2000 measures 0.057524 here, matching the margin quality elsewhere and leaving real headroom
        // rather than sitting close to the boundary.
        await Crowd(engine, 2000);
        await Remember(engine, "floor probe gamma stays moderately aged");
        await Crowd(engine, 3);
        await Remember(engine, "floor probe beta stays lightly aged");
        await Crowd(engine, 2);
        await Remember(engine, "floor probe alpha stays freshest");

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "probe", Limit: 5));

        Assert.DoesNotContain(recalled.Items,
            i => i.Headline.Contains("delta", StringComparison.Ordinal));
        Assert.Equal(
            [
                "floor probe alpha stays freshest",
                "floor probe beta stays lightly aged",
                "floor probe gamma stays moderately aged",
            ],
            recalled.Items.Select(i => i.Headline).ToArray());
    }

    [Fact]
    public async Task Recall_order_over_a_fixed_corpus_needs_relevance_and_retrievability_multiplied()
    {
        // FACT C: pins GraphNode.Relevance's multiplicative contribution against a store where it is NOT a
        // flat 1 — SqliteMemoryGraphStore normalizes it as a rank POSITION (1 - i/count; see
        // MultiplicativeRankingOptions.SalienceRankWeight's own doc). The two candidates are built so their
        // RELEVANCE order (bm25: the repeated term wins) and their RETRIEVABILITY order (recency: the other is
        // fresher) point opposite ways — the asserted order can only be right if both factors are
        // multiplied together, never from either alone. TempDb mirrors SqliteMemoryGraphStoreTests' own
        // per-test fixture.
        //
        // `ranking: new MultiplicativeRankingPolicy()` passed EXPLICITLY (2026-08-11) — this fact's own name
        // and doc are about a PRODUCT ("relevance and retrievability multiplied"), which has no meaning under
        // ReciprocalRankFusionPolicy (the bare constructor's own default as of the same day, owner ruling):
        // RRF never multiplies anything, it sums reciprocal ranks. Left implicit, this fact would silently
        // stop testing what its own name claims. See BuildEngine's own remarks for the identical judgement
        // call made on the same day, for the same reason. UNCHANGED otherwise: this is Multiplicative's own
        // formula, which did not move.
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = new GraphMemoryEngine("project/graph", store, agePolicies: [new PerWriteAgePolicy()],
            ranking: new MultiplicativeRankingPolicy());

        // stronger bm25 match (the term repeats), but aged
        await Remember(engine, "gizmo gizmo gizmo firmware calibration record");
        await Crowd(engine, 9);
        // weaker bm25 match (the term appears once), but freshest
        await Remember(engine, "the firmware calibration team logs a gizmo update note");

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "gizmo", Limit: 5));

        Assert.Equal(
            [
                "gizmo gizmo gizmo firmware calibration record",
                "the firmware calibration team logs a gizmo update note",
            ],
            recalled.Items.Select(i => i.Headline).ToArray());
    }

    [Fact]
    public async Task Recall_order_over_a_fixed_corpus_is_decided_by_retrievability_alone()
    {
        // FACT D: pins Retrievability's OWN multiplicative contribution, isolated from the other three
        // terms — same salience (neutral, default salience policy — SalienceRankWeight stays 0), same hop (both
        // are direct hits; Hops = 0 below keeps the link targets out of recall entirely), and Relevance
        // ties at InMemoryMemoryGraphStore's flat 1 for both. Only Retrievability can move this order.
        // <b>Deliberately does NOT coincide with the id-descending tiebreak</b> the way the other three
        // facts' corpora happen to (Fact 2's re-review, round 2): "anchor" is written FIRST (lower id) but
        // pushed to the connection-boost ceiling, "sensor" is written LAST (higher id) but unconnected —
        // so if Retrievability's contribution were ever dropped, the id tiebreak alone would pick "sensor"
        // first, the WRONG entry, which is what makes the discrimination check below actually fail rather
        // than passing by accident.
        //
        // `ranking: new MultiplicativeRankingPolicy()` passed EXPLICITLY (2026-08-11), for the same reason as
        // Fact C above: this fact's own name and doc claim Retrievability's "MULTIPLICATIVE contribution",
        // which is not a concept RRF (the bare constructor's own default as of the same day) shares. Also
        // measured (not assumed): this specific corpus's own order happens to survive unchanged under RRF
        // too, so pinning Multiplicative explicitly here is about preserving what the fact is FOR, not about
        // an assertion that would otherwise have failed.
        var options = new GraphMemoryOptions { Hops = 0 }; // no expansion: keep the link targets out of recall
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("project/graph", store, options, agePolicies: [new PerWriteAgePolicy()],
            ranking: new MultiplicativeRankingPolicy());

        var anchor = await Remember(engine, "pulse reading from the anchor node");
        var spoke1 = await Remember(engine, "support beam one");
        var spoke2 = await Remember(engine, "support beam two");
        var spoke3 = await Remember(engine, "support beam three");
        await Remember(engine, "pulse reading from the newest sensor"); // higher id, unconnected, freshest
        await Crowd(engine, 15); // ages both further; "anchor" already trails "sensor" by construction

        // heavy connection weight pushes "anchor"'s EffectiveStability to the connection-boost ceiling
        // (DsrOptions.MaxConnectionBoost, 4x) — enough to outweigh its larger age disadvantage
        await engine.LinkAsync(anchor, spoke1, weight: 150, symmetric: true);
        await engine.LinkAsync(anchor, spoke2, weight: 150, symmetric: true);
        await engine.LinkAsync(anchor, spoke3, weight: 150, symmetric: true);

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "pulse", Limit: 5));

        Assert.Equal(
            ["pulse reading from the anchor node", "pulse reading from the newest sensor"],
            recalled.Items.Select(i => i.Headline).ToArray());
    }

    /// <summary>A hostile policy — the only way to prove the ENGINE enforces the authoritative exemption
    /// rather than merely relying on a well-behaved policy to honour it. Drops every candidate it is
    /// given.</summary>
    private sealed class DropEverythingRankingPolicy : IMemoryRankingPolicy
    {
        public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates,
            in MemoryRankingContext context) => Array.Empty<RankedMemory>();
    }

    [Fact]
    public async Task An_authoritative_entry_the_policy_buried_is_still_returned()
    {
        // The exemption is about TRUST, not ranking, so it must hold against a policy that DROPS a candidate
        // outright — including a hostile one that drops everything, proven here. (It is NOT a guarantee
        // against a policy that FABRICATES a replacement under the same id instead of dropping it — the
        // engine's own re-admission is keyed on Node.Id alone, so that class of hostile policy is out of
        // scope; see IMemoryRankingPolicy's own remarks.) This fake drops everything, which is the only way
        // to prove the engine enforces the guarantee rather than relying on the policy to honour it. Registered with a plain
        // AddSingleton BEFORE AddLyntai, which is also the proof that a consumer's own IMemoryRankingPolicy
        // wins over AddMemoryEngine's TryAddSingleton default — see MemoryEngineRegistration.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryRankingPolicy>(new DropEverythingRankingPolicy());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddLyntai(b => b.AddMemoryEngine("m", e => e.UseGraph()));

        var engine = services.BuildServiceProvider()
            .GetRequiredService<IMemoryEngineFactory>().Get("m");

        await engine.RememberAsync(new MemoryWrite("t", "s", "an authoritative fact",
            Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", "an ordinary fact"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "fact"));

        Assert.Contains(recalled.Items, i => i.Headline.Contains("authoritative"));
        Assert.DoesNotContain(recalled.Items, i => i.Headline.Contains("ordinary"));
    }

    /// <summary>Drops every Authoritative candidate outright — mimicking "all fell below the floor" — while
    /// ranking ordinary candidates normally, by descending <c>Node.Id</c>. Unlike
    /// <see cref="DropEverythingRankingPolicy"/> above, which only proves trust holds, this fake keeps enough
    /// of a real policy's shape to pin what the reserve actually does: authoritative candidates the policy
    /// dropped still occupy slots within the caller's limit, displacing the weakest ordinary hits rather than
    /// being cut themselves.</summary>
    private sealed class DropsAuthoritativeRankingPolicy : IMemoryRankingPolicy
    {
        public IReadOnlyList<RankedMemory> Rank(IReadOnlyList<MemoryCandidate> candidates,
            in MemoryRankingContext context) =>
            candidates
                .Where(c => c.Node.Grade != MemoryGrade.Authoritative)
                .OrderByDescending(c => c.Node.Id)
                .Select(c => new RankedMemory(c, 1))
                .ToList();
    }

    /// <summary><b>Authoritative material now takes RESERVED slots: ordinary material is displaced before an
    /// exact fact is.</b>
    /// <para><b>This fact asserted the opposite until 2026-08-13, and its old name said so</b> —
    /// <c>…_and_can_still_be_cut_by_the_limit</c>. That was the "honest reading" of buried-not-cut, stated in
    /// four places and pinned here: a re-admitted entry was appended after the policy's order and cut by the
    /// Take like anything else. The first end-to-end measurement of design §5.7.0's objective (1)
    /// (<c>MemoryAuthoritativeSurvivalTests</c>) showed the cost — ALL THREE authoritative facts lost in ALL
    /// FIVE languages — and §5.7.0 says objective (1) has NO acceptable failure rate. The contract and the
    /// code disagreed; the code was the half that had never been measured.</para>
    /// <para>The old argument ("surviving the limit would let one authoritative entry evict every ordinary
    /// hit") is answered rather than dismissed: it CAN evict ordinary material, because that is what marking
    /// a fact authoritative means and it is the caller's explicit decision — but
    /// <see cref="GraphMemoryOptions.AuthoritativeReserve"/> bounds it, so the promise degrades to "an exact
    /// fact is displaced only by ANOTHER exact fact" rather than to nothing.</para></summary>
    [Fact]
    public async Task Authoritative_entries_take_reserved_slots_and_displace_ordinary_material()
    {
        // Three authoritative facts, all dropped by the fake policy (mimicking "all fell below the floor"),
        // written oldest-to-newest — InMemoryMemoryGraphStore.SeedAsync orders Authoritative candidates by
        // LastRecalledPosition DESC (freshest first), so the reserve fills [newest, middle, oldest]. Two
        // ordinary facts, ranked by the fake policy's own order (Id DESC, so the later write ranks first).
        // Limit = 4 is now spent on the THREE exact facts plus ONE ordinary hit, where it used to be spent
        // on both ordinary hits plus two exact facts.
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("project/graph", store, agePolicies: [new PerWriteAgePolicy()],
            ranking: new DropsAuthoritativeRankingPolicy());

        // authoritative material is admitted regardless of query match, so its content need not mention
        // "gadget" at all — proving inclusion is about the GRADE, not about these facts winning on relevance
        await engine.RememberAsync(new MemoryWrite("t", "s", "oldest exact fact",
            Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", "middle exact fact",
            Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", "newest exact fact",
            Grade: MemoryGrade.Authoritative));
        await Remember(engine, "gadget kept older");
        await Remember(engine, "gadget kept newer");

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "gadget", Limit: 4));

        // every exact fact survives; the weaker ordinary hit is what the limit cuts
        Assert.Equal(
            ["gadget kept newer", "newest exact fact", "middle exact fact", "oldest exact fact"],
            recalled.Items.Select(i => i.Headline).ToArray());
    }

    /// <summary><b><see cref="GraphMemoryOptions.AuthoritativeReserve"/> bounds the displacement</b> — the
    /// answer to the objection the old behaviour was built around. With a reserve of 1 and the same corpus,
    /// only one exact fact takes a slot and three ordinary hits keep theirs.
    /// <para>Setting it to <c>0</c> restores the pre-3.0 behaviour exactly, and re-breaks objective (1) —
    /// which is why it is not the default.</para></summary>
    [Fact]
    public async Task The_authoritative_reserve_bounds_how_much_ordinary_material_is_displaced()
    {
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            options: new GraphMemoryOptions { AuthoritativeReserve = 1 },
            agePolicies: [new PerWriteAgePolicy()],
            ranking: new DropsAuthoritativeRankingPolicy());

        await engine.RememberAsync(new MemoryWrite("t", "s", "oldest exact fact",
            Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", "newest exact fact",
            Grade: MemoryGrade.Authoritative));
        await Remember(engine, "gadget kept older");
        await Remember(engine, "gadget kept newer");

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "gadget", Limit: 3));

        Assert.Equal(
            ["gadget kept newer", "gadget kept older", "newest exact fact"],
            recalled.Items.Select(i => i.Headline).ToArray());
    }

    /// <summary><b>The reserve is bounded by the CALLER's limit, not only by its own value</b> — the two live
    /// on different scopes and nothing else reconciles them.
    /// <see cref="GraphMemoryOptions.AuthoritativeReserve"/> is configured per ENGINE while
    /// <see cref="MemoryQuery.Limit"/> arrives per QUERY, so a reserve chosen against
    /// <see cref="GraphMemoryOptions.DefaultLimit"/> is silently larger than any tighter per-call limit — the
    /// ordinary case, not a pathological one: a caller trimming a prompt budget passes a small
    /// <c>Limit</c> and the engine's own reserve was never told.
    /// <para>Found by the 2026-08-14 review. <c>reserve</c> was <c>Min(authoritative.Count, reserve ?? limit)</c>,
    /// so the <c>?? limit</c> capped only the DEFAULT; an explicit value passed straight through, and the
    /// <c>Take(Math.Max(0, limit - reserve))</c> that follows floors at zero while the reserved list is
    /// concatenated whole. Measured: reserve 5, <c>Limit: 2</c>, three exact facts — <b>three items came back
    /// for a limit of two, and not one ordinary hit</b>. That contradicts all three places the promise is
    /// written down (design §5.7 "within the caller's <c>Limit</c>", <c>README.md</c>, <c>docs/memory.md</c>),
    /// which is what makes it a defect rather than an undocumented corner.</para>
    /// <para>The fix caps the reserve at the limit, so the option can only ever REDUCE displacement — which is
    /// the only direction it is documented to move in. Objective (1) is untouched: the default is still
    /// unbounded-within-the-limit, exactly as before.</para></summary>
    [Fact]
    public async Task A_reserve_larger_than_the_query_limit_still_returns_at_most_the_limit()
    {
        // 5 is a sensible bound against the DEFAULT limit of 10; the caller then asks for 2.
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(),
            options: new GraphMemoryOptions { AuthoritativeReserve = 5 },
            agePolicies: [new PerWriteAgePolicy()],
            ranking: new DropsAuthoritativeRankingPolicy());

        await engine.RememberAsync(new MemoryWrite("t", "s", "oldest exact fact",
            Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", "middle exact fact",
            Grade: MemoryGrade.Authoritative));
        await engine.RememberAsync(new MemoryWrite("t", "s", "newest exact fact",
            Grade: MemoryGrade.Authoritative));
        await Remember(engine, "gadget kept older");
        await Remember(engine, "gadget kept newer");

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "gadget", Limit: 2));

        // the limit still binds, and it is spent on exact facts — objective (1) unchanged, freshest first
        Assert.Equal(
            ["newest exact fact", "middle exact fact"],
            recalled.Items.Select(i => i.Headline).ToArray());
    }
}
