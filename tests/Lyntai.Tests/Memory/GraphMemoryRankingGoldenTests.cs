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
/// CHARACTERIZATION of <see cref="MultiplicativeRankingPolicy"/>'s formula —
/// <c>Relevance × Retrievability × boost × HopAttenuation^hop</c>, then a relative floor — over fixed corpora.
/// Every fact asserts an exact ORDER, not a property: a property test would still pass under a subtly different
/// formula. Every fact passes the policy EXPLICITLY, because its subject is a named formula whose terms have no
/// RRF analogue, not "whatever today's default is" (that subject is <c>MemoryDefaultRecallQualityTests</c>).
/// <para><b>One fact per term</b> — a golden test that exercises only some of a formula's terms lets a rewrite
/// drop or mis-wire the others and still pass (<c>.claude/knowledge/pitfalls.md</c>, "Testing"). Each corpus
/// makes its term DO WORK, shown by perturbing that one term and watching the order move:</para>
/// <list type="bullet">
/// <item><see cref="Recall_order_over_a_fixed_corpus_is_what_it_is_today"/> — <c>HopAttenuation</c>: 0.5 → 0.9
/// turns <c>[wrap-up, seed, hop1, hop2]</c> into <c>[wrap-up, hop1, hop2, seed]</c>.</item>
/// <item><see cref="Recall_order_is_pinned_when_salience_reorders_a_recall"/> — the salience <c>boost</c>:
/// <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/> back at 0 reverts to pure recency.</item>
/// <item><see cref="Recall_order_over_a_fixed_corpus_buries_what_falls_below_the_floor"/> —
/// <see cref="MultiplicativeRankingOptions.RelativeFloor"/>: burial, not just order; a floor of 0 brings the
/// weakest entry back.</item>
/// <item><see cref="Recall_order_over_a_fixed_corpus_needs_relevance_and_retrievability_multiplied"/> —
/// <see cref="GraphNode.Relevance"/> on <see cref="SqliteMemoryGraphStore"/>, where it is a real bm25 rank
/// rather than the in-process store's flat 1; a constant <c>1</c> in its place flips the order.</item>
/// <item><see cref="Recall_order_over_a_fixed_corpus_is_decided_by_retrievability_alone"/> —
/// <c>Retrievability</c> isolated: same salience, same hop, relevance tied, and an order that does NOT
/// coincide with the id-descending tiebreak.</item>
/// </list>
/// </summary>
public sealed class GraphMemoryRankingGoldenTests
{
    /// <summary>An undamped per-write age policy and NO retention policies, so nothing salience-derived can
    /// perturb the baseline, and <see cref="MultiplicativeRankingPolicy"/> passed explicitly (class doc).</summary>
    private static GraphMemoryEngine BuildEngine() =>
        new("project/graph", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Ranking = new MultiplicativeRankingPolicy(),
            });

    /// <summary>A second (or third) view of the SAME store and engine name, differing only in the
    /// salience policy bound to it — the device <c>GraphMemoryRankingTests</c> already uses to give distinct
    /// writes distinct salience, since the salience policy is per-engine-instance rather than per-write. Still
    /// no retention retrievability: <c>SalienceRankWeight</c> is a RANK term read straight off the stored signals bag,
    /// not a retrievability modulation.</summary>
    private static GraphMemoryEngine EngineWithSaliencePolicy(IMemoryGraphStore store, IMemorySaliencePolicy saliencePolicy,
        IMemoryRankingPolicy? ranking = null) =>
        new("project/graph", store, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                SaliencePolicies = [saliencePolicy],
                Ranking = ranking,
            });

    private static async Task<MemoryRef> Remember(GraphMemoryEngine engine, string content) =>
        (await engine.RememberAsync(new MemoryWrite("t", "s", content))).Reference;

    /// <summary>Ages whatever is already stored by writing unrelated material that matches neither the
    /// query nor any link — the same aging device <see cref="GraphMemoryEngineTests"/> uses.</summary>
    private static async Task Crowd(GraphMemoryEngine engine, int writes)
    {
        for (var i = 0; i < writes; i++) await Remember(engine, $"unrelated filler note {i}");
    }

    [Fact]
    public async Task Recall_order_over_a_fixed_corpus_is_what_it_is_today()
    {
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
        // Pins MultiplicativeRankingOptions.SalienceRankWeight's contribution to the `boost` term. Without
        // it, pure recency
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
        // Pins MultiplicativeRankingOptions.RelativeFloor — BURIAL, not just ordering. The default (0.02)
        // excludes nothing in a corpus this size, so the floor here is high enough to cut the weakest entry
        // while the three survivors keep their relative order.
        var ranking = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions { RelativeFloor = 0.1 });
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(), seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Ranking = ranking,
            });

        await Remember(engine, "floor probe delta fades far in the back"); // ages past the floor
        // DsrRetrievability's heavy tail needs a large crowd to fall under a 0.1 floor: 1000 reaches only
        // r≈0.081, too close to the line; 2000 measures r≈0.058, real headroom.
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
        // multiplied together, never from either alone.
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = new GraphMemoryEngine("project/graph", store, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Ranking = new MultiplicativeRankingPolicy(),
            });

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
        // Deliberately does NOT coincide with the id-descending tiebreak the way the other corpora happen to:
        // "anchor" is written FIRST (lower id) but pushed to the connection-boost ceiling, "sensor" is written
        // LAST (higher id) but unconnected — so with Retrievability's contribution dropped, the tiebreak alone
        // would pick "sensor", the WRONG entry.
        var options = new GraphMemoryOptions { Hops = 0 }; // no expansion: keep the link targets out of recall
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("project/graph", store, options, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Ranking = new MultiplicativeRankingPolicy(),
            });

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
        // wins over AddMemoryEngine's TryAddSingleton default — see MemoryEngineBuilderExtensions.
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

    /// <summary><b>Authoritative material takes RESERVED slots: ordinary material is displaced before an exact
    /// fact is</b> — design §5.7.0's objective (1) has NO acceptable failure rate, and
    /// <c>MemoryAuthoritativeSurvivalTests</c> measures it end to end.
    /// <para>It CAN evict ordinary material, because that is what marking a fact authoritative means and it is
    /// the caller's explicit decision; <see cref="GraphMemoryOptions.AuthoritativeReserve"/> bounds how much
    /// (the next fact).</para></summary>
    [Fact]
    public async Task Authoritative_entries_take_reserved_slots_and_displace_ordinary_material()
    {
        // Three authoritative facts, all dropped by the fake policy (mimicking "all fell below the floor"),
        // written oldest-to-newest — InMemoryMemoryGraphStore.SeedAsync orders Authoritative candidates by
        // LastRecalledPosition DESC (freshest first), so the reserve fills [newest, middle, oldest]. Two
        // ordinary facts, ranked by the fake policy's own order (Id DESC, so the later write ranks first).
        // Limit = 4 is spent on the THREE exact facts plus ONE ordinary hit.
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("project/graph", store, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Ranking = new DropsAuthoritativeRankingPolicy(),
            });

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

    /// <summary><b><see cref="GraphMemoryOptions.AuthoritativeReserve"/> bounds the displacement.</b> With a
    /// reserve of 1, only one exact fact takes a slot and the ordinary hits keep theirs. A reserve of <c>0</c>
    /// breaks objective (1), which is why it is not the default.</summary>
    [Fact]
    public async Task The_authoritative_reserve_bounds_how_much_ordinary_material_is_displaced()
    {
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(), options: new GraphMemoryOptions { AuthoritativeReserve = 1 }, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Ranking = new DropsAuthoritativeRankingPolicy(),
            });

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
    /// <c>Limit</c> and the engine's own reserve is never told.
    /// <para>The reserve is capped at the limit, so the option can only ever REDUCE displacement — the only
    /// direction design §5.7 ("within the caller's <c>Limit</c>") documents. Uncapped, reserve 5 with
    /// <c>Limit: 2</c> and three exact facts returns three items for a limit of two.</para></summary>
    [Fact]
    public async Task A_reserve_larger_than_the_query_limit_still_returns_at_most_the_limit()
    {
        // 5 is a sensible bound against the DEFAULT limit of 10; the caller then asks for 2.
        var engine = new GraphMemoryEngine("project/graph", new InMemoryMemoryGraphStore(), options: new GraphMemoryOptions { AuthoritativeReserve = 5 }, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Ranking = new DropsAuthoritativeRankingPolicy(),
            });

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
