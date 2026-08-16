using System.Globalization;
using Lyntai.Cortex;
using Lyntai.Memory;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Storage;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>The graph engine reached through the MEM1 seam: registered as a member like any other, blended
/// with an authoritative curated member, and expandable through the blend.</summary>
public class GraphMemoryWiringTests
{
    /// <summary>Reports a fixed salience for content containing <paramref name="marker"/> and declines
    /// otherwise — lets a container-level test distinguish "salient" writes without depending on the
    /// default salience policy's novelty curve (already covered by <see cref="SalienceTests"/>).</summary>
    private sealed class MarksContentSalient(string marker, double salience) : IMemorySaliencePolicy
    {
        // a fake's own bit, from the consumer range (32-62) — never None: fix round 2's provenance
        // validation rejects a policy declaring None, since every REAL, running policy has an identity.
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context) =>
            write.Content.Contains(marker, StringComparison.Ordinal)
                ? MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, salience)
                : MemorySignals.Empty;
    }

    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<ICuratedMemoryStore>(new FakeCuratedStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            configure(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task UseGraph_wires_a_working_graph_engine()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseGraph()));

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph");
        await engine.RememberAsync(new MemoryWrite("t", "s", "the build gate runs seven checks"));
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "gate"));

        Assert.Single(recall.Items);
        Assert.Equal(MemorySources.Graph, recall.Ran);
    }

    /// <summary><b>Two named engines carry INDEPENDENT configuration sets — that is the point of naming
    /// them, and nothing asserted it until now.</b>
    ///
    /// <para>`AddMemoryEngine` is modelled on `IHttpClientFactory` (<c>docs/DECISIONS.md</c> <b>D39</b>): a
    /// name resolves a configured instance. A design that names instances but shares one option set would
    /// be a factory in spelling only, and the failure would be silent — every engine quietly behaving like
    /// whichever registration ran last.</para>
    ///
    /// <para>Uses <see cref="GraphMemoryOptions.ReinforceOn"/> as the observable because both positions are
    /// visible through the PUBLIC surface: a recall that reinforces resets the entry's age, so its
    /// retrievability returns to 1, while one that reinforces nothing leaves it decayed. An earlier draft
    /// used <see cref="GraphMemoryOptions.Reinforcement"/> and measured nothing — the age reset drives
    /// retrievability to 1 whether or not stability grew, so both arms looked identical through the only
    /// number a consumer can actually read.</para></summary>
    [Fact]
    public async Task Two_named_engines_carry_independent_option_sets()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("learns", e => e.UseGraph(
                new GraphMemoryOptions { ReinforceOn = MemoryReinforcementActs.All }))
            .AddMemoryEngine("frozen", e => e.UseGraph(
                new GraphMemoryOptions { ReinforceOn = MemoryReinforcementActs.None })));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();

        var learns = await RetrievabilityAcrossRecallAsync(factory.Get("learns/graph"));
        var frozen = await RetrievabilityAcrossRecallAsync(factory.Get("frozen/graph"));

        // the entry must have DECAYED first, or "returned to 1" would prove nothing
        Assert.True(learns.Before < 1, $"the entry never aged; before was {learns.Before}");

        Assert.Equal(1, learns.After, precision: 9);                        // reinforced: age reset
        Assert.Equal(frozen.Before, frozen.After, precision: 9);            // untouched by its own recall
    }

    /// <summary>Writes one entry, ages it behind filler, recalls it, and reports its retrievability either
    /// side. Reads through <see cref="IExpandableMemory"/> rather than a store handle, so it exercises
    /// whatever the named engine actually resolved to — which is the thing under test.</summary>
    private static async Task<(double Before, double After)> RetrievabilityAcrossRecallAsync(IMemoryEngine engine)
    {
        var reference = await engine.RememberAsync(
            new MemoryWrite("t", "s", "the deploy pipeline requires manual approval"));
        for (var i = 0; i < 8; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"unrelated filler entry number {i}"));

        var before = await RetrievabilityOfAsync(engine, reference);
        Assert.NotEmpty((await engine.RecallAsync(new MemoryQuery("t", "s", "deploy pipeline"))).Items);
        return (before, await RetrievabilityOfAsync(engine, reference));
    }

    /// <summary>Read back through the engine's own expansion — the shape a consumer has, rather than a
    /// store handle a wiring test happens to hold. <c>hops: 0</c> so nothing but the entry itself returns.
    /// <para><b>Expansion does not disturb the measurement</b> in either arm: the reinforcing engine has
    /// already had its age reset by the recall, and the frozen one reinforces on no act at all.</para></summary>
    private static async Task<double> RetrievabilityOfAsync(IMemoryEngine engine, MemoryRef reference)
    {
        var expanded = await ((IExpandableMemory)engine).ExpandAsync(reference, hops: 0);
        return expanded.Items[0].Retrievability;
    }

    [Theory]
    [InlineData("UseGraph", "project/graph")]
    [InlineData("AddMemory", "project/memory")]
    public async Task The_default_salience_modulation_is_applied_through_the_container(
        string entryPoint, string member)
    {
        // proves the ModulatedRetrievability wrap is actually applied for a consumer who never touches
        // IMemoryRetrievabilityPolicy or IMemorySaliencePolicy directly — every other salience test builds its
        // GraphMemoryEngine BY HAND with an explicit policy, so deleting the wrap in the builder would leave
        // the whole feature dead for every DI consumer while the rest of the suite stayed green.
        //
        // BOTH entry points, because they are two SEPARATE wraps in MemoryEngineBuilder — UseGraph and the
        // UseBestAvailable that AddMemory() routes through — and covering only the first left the zero-
        // configuration path (the one the README leads with) able to lose the feature silently. The existing
        // AddMemory facts assert only which engine TYPE was picked, which the wrap does not affect.
        //
        // A custom salience policy stands in for the default so the assertion doesn't also depend on
        // StructuralSaliencePolicy's novelty curve (SalienceTests already covers that).
        // A plain PerWriteAgePolicy, not the engine's default BurstDampenedAgePolicy: the container resolves ONE
        // engine instance (a singleton) sharing ONE age policy across every write, and burst damping would give
        // "the salient one" (write #1 in the burst) a stronger STORED stability than any later interference
        // write purely from encoding, independently of salience — confounding the very thing this test
        // means to isolate. PerWriteAgePolicy encodes every write identically, so the only stability difference
        // left is the one salience causes.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddSingleton<IMemoryAgePolicy>(new PerWriteAgePolicy());
        services.AddSingleton<IMemorySaliencePolicy>(new MarksContentSalient("salient", 4));
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            if (entryPoint == "UseGraph") b.AddMemoryEngine("project", e => e.UseGraph());
            else b.AddMemory("project");
        });
        using var sp = services.BuildServiceProvider();

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get(member);
        await engine.RememberAsync(new MemoryWrite("t", "s", "the salient one"));
        for (var i = 0; i < 40; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"interference {i}"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", Limit: 50));

        // "the salient one" is written FIRST, so it is one advance OLDER than "interference 0" — an age
        // handicap working against this assertion, exactly like GraphMemorySalienceTests's hand-built
        // version. Salience must overcome it for this to pass.
        var high = recalled.Items.Single(i => i.Headline.Contains("the salient one"));
        var low = recalled.Items.Single(i => i.Headline.Contains("interference 0"));
        Assert.True(high.Retrievability > low.Retrievability,
            $"salient {high.Retrievability} should exceed ordinary {low.Retrievability}");
    }

    [Fact]
    public void UseGraph_lets_a_consumer_registered_salience_policy_win_over_the_default()
    {
        // what TryAddSingleton promises: a consumer's own IMemorySaliencePolicy must win, and a second
        // AddMemoryEngine call must not pile a second default on top of it (see MemoryEngineRegistration)
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        var custom = new MarksContentSalient("x", 2);
        services.AddSingleton<IMemorySaliencePolicy>(custom);
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));
        using var sp = services.BuildServiceProvider();

        Assert.Same(custom, sp.GetRequiredService<IMemorySaliencePolicy>());
    }

    [Fact]
    public void UseGraph_a_consumer_registered_salience_policy_added_AFTER_AddLyntai_coexists_with_the_default_instead_of_replacing_it()
    {
        // The mirror of the fact above, and a DELIBERATE, tested asymmetry (fix round 1, I-2). Before Task 3,
        // IMemorySaliencePolicy was resolved with GetService (single) — which returns the LAST registration
        // regardless of ordering, so registering after AddLyntai used to WIN just as cleanly as registering
        // before it. Now that salience policies are plural (GetServices), that is no longer true: TryAddSingleton
        // only skips its OWN registration when one ALREADY EXISTS at the moment it runs. Registered before
        // AddLyntai (the fact above), the consumer's own salience policy wins outright — TryAddSingleton sees an
        // existing registration and never adds the default at all. Registered AFTER, TryAddSingleton has
        // ALREADY seeded the default, so this is a genuine SECOND registration into the collection —
        // GetServices returns BOTH, and the engine's own default composition (MaximalSalienceCompositionPolicy)
        // combines them per signal name rather than either replacing the other. A consumer who wants a pure
        // replacement, whichever way they register, registers BEFORE AddLyntai.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));
        var custom = new MarksContentSalient("x", 2);
        services.AddSingleton<IMemorySaliencePolicy>(custom); // registered AFTER AddLyntai
        using var sp = services.BuildServiceProvider();

        var registered = sp.GetServices<IMemorySaliencePolicy>().ToList();
        Assert.Equal(2, registered.Count);
        Assert.Contains(custom, registered);
        Assert.Contains(registered, p => p is StructuralSaliencePolicy);
    }

    [Fact]
    public void UseGraph_lets_a_consumer_registered_ranking_policy_win_over_the_default()
    {
        // The same TryAdd promise as the salience-policy fact above, for the newer ranking seam: a
        // consumer's own IMemoryRankingPolicy must win over AddMemoryEngine's TryAddSingleton default
        // (ReciprocalRankFusionPolicy as of 3.0, owner ruling 2026-08-11 — was MultiplicativeRankingPolicy;
        // see MemoryEngineRegistration) — proven here by registering the OTHER policy, which discriminates
        // regardless of which one is currently the default: Assert.Same below checks object IDENTITY, so
        // this fact is unaffected by a future default change either way. Registered exactly ONE policy is
        // ever consulted (GetService, not GetServices), so — unlike the salience and retention policy collections
        // — there is nothing to pile up on a second AddMemoryEngine call to check here.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        var custom = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions { HopAttenuation = 0.9 });
        services.AddSingleton<IMemoryRankingPolicy>(custom);
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));
        using var sp = services.BuildServiceProvider();

        Assert.Same(custom, sp.GetRequiredService<IMemoryRankingPolicy>());
    }

    [Fact]
    public void UseGraph_lets_a_consumer_registered_ranking_policy_win_whether_registered_before_or_after_AddLyntai()
    {
        // The fact above only proves the BEFORE direction. IMemoryEngine/IMemoryEngineFactory are registered
        // as FACTORIES (`sp => engineBuilder.Build(sp)`), so IMemoryRankingPolicy is not actually resolved
        // until the container is asked for an IMemoryEngine — by which point `sp` is the fully-built
        // provider, holding every registration regardless of when it was added. GetService<T> against
        // multiple registrations of the same service returns the LAST one, so a consumer's own
        // AddSingleton<IMemoryRankingPolicy> registered AFTER AddLyntai still wins over
        // MemoryEngineRegistration's own TryAddSingleton default — mirrors
        // A_consumer_registered_retrievability_policy_wins_whether_it_is_registered_before_or_after_AddLyntai,
        // which covers both directions for exactly this reason (a design different from the DeadHostTracker trap
        // in .claude/knowledge/pitfalls.md, where a TryAddSingleton reached DURING configure(builder) beats a
        // registration AddLyntai itself makes later — nothing here is resolved that early).
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));
        var custom = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions { HopAttenuation = 0.9 });
        services.AddSingleton<IMemoryRankingPolicy>(custom); // registered AFTER AddLyntai
        using var sp = services.BuildServiceProvider();

        Assert.Same(custom, sp.GetRequiredService<IMemoryRankingPolicy>());
    }

    [Fact]
    public void UseGraph_registers_a_default_ranking_policy_in_the_container()
    {
        // GraphMemoryEngine's OWN "ranking ?? new ReciprocalRankFusionPolicy()" constructor fallback (was
        // MultiplicativeRankingPolicy before the 2026-08-11 owner ruling — see MemoryEngineRegistration's own
        // remarks) would mask either of two regressions from ever showing up in a RECALL-BEHAVIOUR test:
        // deleting MemoryEngineRegistration's TryAddSingleton<IMemoryRankingPolicy>, or deleting the
        // `ranking:` argument MemoryEngineBuilder.UseGraph passes the engine — either way RecallAsync's
        // OUTPUT looks identical (the engine just builds its own fallback instance instead of the
        // container's). The only way to catch the FIRST of those two is to check the CONTAINER directly,
        // with nothing else in this test registering an IMemoryRankingPolicy of its own — GetRequiredService
        // throws if TryAddSingleton is gone and nothing else filled the slot. The SECOND is caught by
        // An_authoritative_entry_the_policy_buried_is_still_returned (GraphMemoryRankingGoldenTests): its
        // hostile DropEverythingRankingPolicy only reaches the engine's OUTPUT through the `ranking:` wire,
        // so deleting that argument makes the ordinary fact reappear and fails its second assertion.
        //
        // A type check only, deliberately cheap and narrow — see
        // The_zero_configuration_default_is_ReciprocalRankFusionPolicy below for the stronger, mutation-
        // checked, recall-BEHAVIOUR proof that the resolved type is actually what the engine uses, not merely
        // what the container hands back.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));
        using var sp = services.BuildServiceProvider();

        Assert.IsType<ReciprocalRankFusionPolicy>(sp.GetRequiredService<IMemoryRankingPolicy>());
    }

    [Fact]
    public async Task The_zero_configuration_default_is_ReciprocalRankFusionPolicy()
    {
        // The same trap the forgetting-curve default fell into once already (fix round, 2026-08-10):
        // registering the NOW-default policy and asserting the engine uses it proves nothing once that
        // policy IS the default — every fact above that registers Multiplicative now discriminates AGAINST
        // the default (a genuine restore-path test), and the type-check above is a narrow, cheap proxy for
        // "was TryAdd deleted." This is the one fact that registers NOTHING for IMemoryRankingPolicy and
        // checks, by ACTUAL RECALL ORDER (not merely a resolved type), what a consumer who configures
        // nothing gets — and that it genuinely differs from what Multiplicative would have produced on the
        // identical input, not merely that "a policy" ran.
        //
        // Reuses GraphMemoryRankingGoldenTests's own Fact C corpus verbatim (two candidates whose RELEVANCE
        // and RETRIEVABILITY orders point opposite ways: "gizmo gizmo gizmo..." has the stronger bm25 match
        // but is aged; "the firmware calibration team..." is the weaker match but freshest) — chosen there to
        // prove Multiplicative's PRODUCT needs both factors; it turns out to ALSO be exactly the shape that
        // makes Multiplicative and RRF genuinely DISAGREE on the winner, not just on the score. Under
        // Multiplicative's product, the older-but-more-relevant entry wins (Fact C, unchanged, still true).
        // Under RRF at the shipped default weights (all equal), the two candidates' relevance-rank and
        // retrievability-rank terms are EXACT MIRROR IMAGES of each other (rank 1 vs 2, swapped) — so the two
        // signals' contributions sum to a byte-identical TIE, decided by the tiebreak (Node.Id DESCENDING),
        // which favours the LATER write instead. MEASURED: Multiplicative orders
        // [aged-but-relevant, fresh-but-weaker] (Fact C); RRF orders [fresh-but-weaker, aged-but-relevant] —
        // genuinely different winners from the SAME corpus, which is what makes this proof rather than
        // coincidence.
        //
        // MUTATION-CHECKED, TWO WAYS — the first attempt's OWN result corrected the doc comment rather than
        // being smoothed over to match what was expected going in.
        //
        // Attempt 1 (task brief's own suggestion): commented out MemoryEngineRegistration's own
        // `TryAddSingleton<IMemoryRankingPolicy>` line entirely. This fact did NOT fail —
        // `UseGraph`'s own `sp.GetService<IMemoryRankingPolicy>()` call (GetService, not
        // GetRequiredService — it returns null rather than throwing) hands `ranking: null` to
        // `GraphMemoryEngine`'s constructor, whose OWN bare-constructor fallback (`ranking ?? new
        // ReciprocalRankFusionPolicy()`, changed to match the very same task) silently supplies RRF anyway —
        // masking the missing DI registration from a RECALL-BEHAVIOUR check by construction, the exact
        // failure mode `UseGraph_registers_a_default_ranking_policy_in_the_container`'s own doc already
        // names as the reason THAT test has to check the CONTAINER directly (`GetRequiredService`, which DOES
        // throw). The two tests are complementary for exactly this reason, not redundant: that one proves the
        // DI REGISTRATION exists; this one proves what it produces is recall-distinguishable from
        // Multiplicative — neither can prove what the other does.
        //
        // Attempt 2 (the one that actually discriminates this fact from a tautology): kept the
        // `TryAddSingleton` registration in place but changed WHAT it supplies —
        // `new MultiplicativeRankingPolicy(sp.GetService<MultiplicativeRankingOptions>())` instead of
        // RRF. This fact FAILED exactly as expected: the observed order flipped to Multiplicative's own
        // (`[aged-but-relevant, fresh-but-weaker]`), proving the assertion is genuinely sensitive to what the
        // DI default supplies, not passing by coincidence of the bare-constructor fallback agreeing with it.
        // Reverted; re-ran; passes again.
        using var db = new TempDb();
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new SqliteMemoryGraphStore(db.Factory));
        // An undamped age policy, exactly like Fact C's own direct construction — without it, UseGraph's DEFAULT
        // IMemoryAgePolicy (BurstDampenedAgePolicy) would flatten this fast in-process replay's whole age
        // signal inside one wall-clock burst window, degenerating the very retrievability-rank disagreement
        // this fact depends on (the identical substitution MemoryDefaultRecallQualityTests's own class doc
        // gives the same reasoning for).
        services.AddSingleton<IMemoryAgePolicy>(new PerWriteAgePolicy());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));
        // deliberately NO services.AddSingleton<IMemoryRankingPolicy>(...) call anywhere
        using var sp = services.BuildServiceProvider();
        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("project/graph");

        await engine.RememberAsync(new MemoryWrite("t", "s", "gizmo gizmo gizmo firmware calibration record"));
        for (var i = 0; i < 9; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"unrelated filler note {i}"));
        await engine.RememberAsync(
            new MemoryWrite("t", "s", "the firmware calibration team logs a gizmo update note"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "gizmo", Limit: 5));

        // RRF's own order — the reverse of what Multiplicative produces on this identical corpus (Fact C).
        Assert.Equal(
            [
                "the firmware calibration team logs a gizmo update note",
                "gizmo gizmo gizmo firmware calibration record",
            ],
            recalled.Items.Select(i => i.Headline).ToArray());
    }

    [Fact]
    public void A_registered_SalienceOptions_reaches_BOTH_the_salience_and_the_retention_policy()
    {
        // The container is the ONLY configuration path SalienceOptions has — no builder surface, unlike
        // GraphMemoryOptions. SalienceRetentionPolicy was registered by TYPE (so DI injected
        // the options into its optional parameter) while the salience policy was registered by a factory that
        // hardcoded null, so a registered SalienceOptions reached exactly one of the two types whose whole
        // documented contract is that their bounds "cannot drift apart" — and SalienceTests, which asserts
        // that coupling by constructing both BY HAND, could not see it.
        //
        // NoveltyWeight is raised so MaxSalience is actually load-bearing: at the default 1.5 the salience
        // policy tops out at 2.5 and a ceiling of 7 would never be reached, so the assertion would pass against
        // a salience policy that ignored the options entirely.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddSingleton(new SalienceOptions { MaxSalience = 7, NoveltyWeight = 100 });
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));
        using var sp = services.BuildServiceProvider();

        var highest = sp.GetRequiredService<IMemorySaliencePolicy>()
            .Signals(new MemoryWrite("t", "s", "x"),
                new SalienceContext("project/graph", Novelty: 1, ComparableCount: 50))
            .Get(MemorySignals.WellKnown.Salience);
        var retentionPolicy = Assert.Single(sp.GetServices<IMemoryRetentionPolicy>());

        Assert.Equal(7, retentionPolicy.MaxStabilityFactor, 6);
        Assert.Equal(7, highest, 6);
    }

    [Fact]
    public void A_registered_MultiplicativeRankingOptions_reaches_an_explicitly_restored_MultiplicativeRankingPolicy()
    {
        // MultiplicativeRankingPolicy is no longer the DI DEFAULT as of 3.0 — drift-ok: names the retired
        // default deliberately. ReciprocalRankFusionPolicy is; see MemoryEngineRegistration's own remarks.
        // This fact used to prove
        // a registered MultiplicativeRankingOptions reached the (then-default) policy with NOTHING else
        // registered; that shape went from "discriminating" to "silently wrong" the moment the default
        // changed — registering ONLY the options record now reaches nothing at all, since the default
        // policy (RRF) does not read MultiplicativeRankingOptions. Restructured into the regression test for
        // the DOCUMENTED RESTORE PATH instead (mirrors how A_registered_DsrRetrievability_is_what_the_engine_
        // actually_uses stopped being an override-precedence test the moment DsrRetrievability became
        // default, and was kept as a plumbing-correctness test in its own right): a consumer who wants
        // Multiplicative back registers it via the OPTIONS-aware factory shape below — the one-line restore —
        // and this proves that shape still reaches Multiplicative's own arithmetic correctly.
        //
        // MultiplicativeRankingPolicy exposes no public reader for its own options (by design — see the
        // class doc on why a policy's score scale is its own business), so this proves the option reached
        // it BEHAVIOURALLY: HopAttenuation raised to 0.9 is asserted directly on a hop-1 candidate's score,
        // which is exactly HopAttenuation at Relevance = Retrievability = 1 and the shipped
        // SalienceRankWeight = 0 (boost = 1) — at the UNREGISTERED default (0.5) this would read 0.5, not 0.9.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddSingleton(new MultiplicativeRankingOptions { HopAttenuation = 0.9 });
        // THE ONE-LINE RESTORE, options-aware: a bare `new MultiplicativeRankingPolicy()` would win over the
        // TryAdd default too, but would NOT read a separately-registered MultiplicativeRankingOptions — this
        // sp-lambda shape is what a consumer who also wants their own options honoured actually registers.
        services.AddSingleton<IMemoryRankingPolicy>(sp =>
            new MultiplicativeRankingPolicy(sp.GetService<MultiplicativeRankingOptions>()));
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));
        using var sp = services.BuildServiceProvider();

        var node = new GraphNode(1, "project/graph", "t", "s", "h", "c", MemoryGrade.Associative,
            DateTimeOffset.UtcNow, 0, 1, 0, Relevance: 1, Degree: 0, Metadata: null);
        var ranked = sp.GetRequiredService<IMemoryRankingPolicy>()
            .Rank([new MemoryCandidate(node, Retrievability: 1, Hop: 1)],
                new MemoryRankingContext(Limit: 10, Engine: "project/graph"));

        Assert.Equal(0.9, Assert.Single(ranked).Score, 9);
    }

    [Fact]
    public void UseGraph_without_a_graph_store_fails_at_startup_naming_it()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseGraph()));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.BuildServiceProvider().GetRequiredService<IMemoryEngineFactory>());

        Assert.Contains("IMemoryGraphStore", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddMemory_prefers_the_graph_when_a_graph_store_is_present()
    {
        using var sp = Build(cfg => cfg.AddMemory());

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get();
        var member = ((CompositeMemoryEngine)engine).Members[0];

        Assert.IsType<Lyntai.Memory.Engines.GraphMemoryEngine>(member);
    }

    [Fact]
    public void AddMemory_falls_back_to_the_keyword_store_when_there_is_no_graph_store()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddLyntai(b => b.AddProvider(_ => new FakeLlmProvider("p")).AddMemory());
        using var sp = services.BuildServiceProvider();

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get();
        var member = ((CompositeMemoryEngine)engine).Members[0];

        Assert.IsType<Lyntai.Memory.Engines.LexicalMemoryEngine>(member);
    }

    [Fact]
    public async Task A_graph_member_blends_with_an_authoritative_curated_member()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("project", e => e
                .UseCurated("glossary").ReserveCharacters(200)
                .UseGraph()
                .Budget(600))
            .UseMemoryComposer("project"));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();
        await factory.Get("project/glossary").RememberAsync(new MemoryWrite("t", "s",
            "the build gate is dev.mjs verify", Grade: MemoryGrade.Authoritative));
        for (var i = 0; i < 50; i++)
            await factory.Get("project/graph").RememberAsync(
                new MemoryWrite("t", "s", $"gate related chatter number {i} at some length"));

        var composed = await sp.GetRequiredService<IPromptComposer>()
            .ComposeAsync("BASE", "t", "s", "gate");

        Assert.Contains("the build gate is dev.mjs verify", composed, StringComparison.Ordinal);
        Assert.Contains("## Recalled context (associative", composed, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Expansion_routes_through_the_blend_to_the_graph_member()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseCurated().UseGraph()));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();
        var blend = (IExpandableMemory)factory.Get("project");
        var reference = await factory.Get("project/graph").RememberAsync(
            new MemoryWrite("t", "s", "a long fact whose content is withheld until it is expanded"));

        var expanded = await blend.ExpandAsync(reference);

        Assert.Contains("withheld until it is expanded", expanded.Items[0].Content!,
            StringComparison.Ordinal);
    }

    /// <summary>Reads back the entry's own decay state and evaluates the curve under test against it, rather
    /// than against a hardcoded age with no necessary relationship to the engine's real one — the state the
    /// engine used and the state the assertion checks against must be the SAME MemoryDecayState, or the
    /// comparison is not about which policy was selected at all.</summary>
    private static async Task<(MemoryItem Item, MemoryDecayState State)> RecallAFactAfterInterference(
        IMemoryEngineFactory factory, IMemoryGraphStore store, string engineName)
    {
        var engine = factory.Get(engineName);
        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", "a fact"));
        for (var i = 0; i < 60; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"interference {i}"));

        // Read the decay state back BEFORE recall, not after: RecallAsync reinforces (and so re-stamps
        // the position of) every item it returns, so reading the store back afterwards would see the
        // entry freshly touched — age reset to zero, stability already grown by Reinforce — rather than
        // the aged state recall actually ranked against when it computed item.Retrievability.
        var id = long.Parse(reference.Id, NumberStyles.Integer, CultureInfo.InvariantCulture);
        var node = await store.GetAsync(reference.Engine, id);
        Assert.NotNull(node);
        var state = node!.DecayState;

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", "a fact"));
        var item = Assert.Single(recalled.Items, i => i.Headline.Contains("a fact"));

        return (item, state);
    }

    [Fact]
    public async Task A_registered_DsrRetrievability_is_what_the_engine_actually_uses()
    {
        // The whole promise of the seam: swapping the forgetting model is a registration, not a fork.
        //
        // The threshold here is NOT a hardcoded age — it is the entry's own MemoryDecayState, read back
        // from the store, so DsrRetrievability is evaluated at the SAME state the engine actually produced.
        // A fixed constant (e.g. comparing against a hand-picked MemoryDecayState(60, 0, 20)) would be a
        // statement about one arbitrary age, not about which policy the engine is using — and matching to 12
        // decimal places (below) is itself strong enough evidence that the engine evaluated this exact
        // formula: a different formula agreeing with DSR to 1e-12 at an arbitrary state would be a
        // coincidence with no plausible cause.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        var store = new InMemoryMemoryGraphStore();
        services.AddSingleton<IMemoryGraphStore>(store);
        services.AddSingleton<IMemoryAgePolicy>(new PerWriteAgePolicy());
        services.AddSingleton<IMemoryRetrievabilityPolicy>(new DsrRetrievability());
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            b.AddMemoryEngine("m", e => e.UseGraph());
        });
        using var sp = services.BuildServiceProvider();

        var (item, state) = await RecallAFactAfterInterference(
            sp.GetRequiredService<IMemoryEngineFactory>(), store, "m");

        var dsr = new DsrRetrievability().Retrievability(state);

        // proves the engine actually EVALUATED the registered policy, not merely that recall still works:
        // what the engine reported must match DSR evaluated at the SAME state, bit for bit.
        Assert.Equal(dsr, item.Retrievability, 12);
    }

    /// <summary>A distinguishable, test-only stand-in for "a consumer's own, non-default retrievability
    /// policy" — replaces the deleted <c>HalfLifeRetrievability</c> (<c>docs/DECISIONS.md</c> D49) as the
    /// NON-default this fact needs to prove a consumer's own registration wins, without resurrecting a
    /// shipped curve's own arithmetic (2026-08-10, fsrs-properly plan Task 1). Ignoring the state entirely
    /// and returning a fixed value is deliberate: nothing about this fact depends on the fake's own curve
    /// SHAPE, only on its output being unmistakably not whatever DSR would compute.</summary>
    private sealed class ConstantRetrievability(double value) : IMemoryRetrievabilityPolicy
    {
        public double InitialStability => 20;
        public MemoryRetrievabilityProvenance Provenance => (MemoryRetrievabilityProvenance)(1L << 32);
        public double Retrievability(in MemoryDecayState state) => value;
        public MemoryDecayState Reinforce(in MemoryDecayState state) => state;
        public double CandidateCutoff(double minRetrievability) => double.PositiveInfinity;
    }

    [Fact]
    public async Task A_consumer_registered_retrievability_policy_wins_whether_it_is_registered_before_or_after_AddLyntai()
    {
        // The DeadHostTracker trap (.claude/knowledge/pitfalls.md, DI/config) is that a TryAddSingleton
        // reached DURING configure(builder) beats AddLyntai's own later registration — so registration
        // order can matter. It does NOT matter here: AddMemoryEngine's callback DOES TryAdd a default
        // IMemoryRetrievabilityPolicy (DsrRetrievability) alongside the existing IMemorySaliencePolicy/
        // IMemoryRetentionPolicy/IMemoryRankingPolicy defaults — but TryAddSingleton only ever adds the FIRST
        // registration for a type, so this test's own explicit AddSingleton below (called AFTER AddLyntai) is
        // a genuine SECOND registration, and GetService<T> against multiple registrations of the same
        // service returns the LAST one — the test's own, not the TryAdd default. UseGraph reads it with a
        // plain GetRequiredService<IMemoryRetrievabilityPolicy>() at container-BUILD time, by which point
        // both registrations already exist, so there is nothing to race against either way. Proven
        // empirically, registered AFTER AddLyntai (the direction the trap above would break).
        //
        // Registers a value DsrRetrievability could never produce, deliberately — Dsr is the default (see
        // below), so a fact that registers Dsr passes whether or not the registration had any effect at all,
        // which is exactly the vacuity a fix-round review caught. Registering something DSR could not have
        // produced is what makes this discriminate.
        const double distinguishableValue = 0.42;
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        var store = new InMemoryMemoryGraphStore();
        services.AddSingleton<IMemoryGraphStore>(store);
        services.AddSingleton<IMemoryAgePolicy>(new PerWriteAgePolicy());
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            b.AddMemoryEngine("m", e => e.UseGraph());
        });
        services.AddSingleton<IMemoryRetrievabilityPolicy>(
            new ConstantRetrievability(distinguishableValue)); // registered AFTER AddLyntai
        using var sp = services.BuildServiceProvider();

        var (item, _) = await RecallAFactAfterInterference(
            sp.GetRequiredService<IMemoryEngineFactory>(), store, "m");

        Assert.Equal(distinguishableValue, item.Retrievability, 12);
    }

    [Fact]
    public async Task The_zero_configuration_default_is_DsrRetrievability()
    {
        // C1 (fix round, 2026-08-10): the whole behaviour change D49 ships — DsrRetrievability becoming the
        // REGISTERED default — had no coverage. Every existing fact in this file either registered Dsr
        // explicitly (which now passes whether or not the registration mattered, since Dsr is the default
        // either way) or bypassed DI entirely. This is the one fact that registers NOTHING for
        // IMemoryRetrievabilityPolicy and checks what a consumer who configures nothing actually gets.
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        var store = new InMemoryMemoryGraphStore();
        services.AddSingleton<IMemoryGraphStore>(store);
        services.AddSingleton<IMemoryAgePolicy>(new PerWriteAgePolicy());
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            b.AddMemoryEngine("m", e => e.UseGraph());
        });
        // deliberately NO services.AddSingleton<IMemoryRetrievabilityPolicy>(...) call anywhere
        using var sp = services.BuildServiceProvider();

        var (item, state) = await RecallAFactAfterInterference(
            sp.GetRequiredService<IMemoryEngineFactory>(), store, "m");

        var dsr = new DsrRetrievability().Retrievability(state);

        // proves the engine actually EVALUATED the zero-config default, not merely that recall still works:
        // what the engine reported must match DSR evaluated at the SAME state, bit for bit.
        Assert.Equal(dsr, item.Retrievability, 12);
    }

    /// <summary>Records every write it was asked about, so a test can prove the engine CONSULTED it rather
    /// than merely that recall still worked.</summary>
    private sealed class RecordingAnnotation : Lyntai.Memory.Annotation.IMemoryAnnotationPolicy
    {
        public List<string> Seen { get; } = [];

        public Task<Lyntai.Memory.Annotation.MemoryAnnotation> AnnotateAsync(
            Lyntai.Memory.Annotation.MemoryAnnotationRequest request, CancellationToken ct = default)
        {
            Seen.Add(request.Write.Content);
            return Task.FromResult(new Lyntai.Memory.Annotation.MemoryAnnotation(["owner"], null));
        }
    }

    /// <summary>Records every query it was asked about, and judges nothing — so it changes no ordering and
    /// the only thing it can fail is "was it consulted at all".</summary>
    private sealed class RecordingVerification : Lyntai.Memory.Verification.IMemoryVerificationPolicy
    {
        public List<string> Seen { get; } = [];

        public Task<Lyntai.Memory.Verification.MemoryVerification> VerifyAsync(
            Lyntai.Memory.Verification.MemoryVerificationRequest request, CancellationToken ct = default)
        {
            Seen.Add(request.Query);
            return Task.FromResult(Lyntai.Memory.Verification.MemoryVerification.NoOpinion);
        }
    }

    /// <summary><b>The ONE-LINE path must honour a container registration exactly as the configured path
    /// does.</b>
    /// <para>It did not. <c>MemoryEngineBuilder.UseBestAvailable</c> — what <c>AddMemory()</c> resolves to,
    /// and the path this library documents as "the one-line path, and deliberately so" — constructed
    /// <see cref="Lyntai.Memory.Engines.GraphMemoryEngine"/> without passing <c>annotation:</c> or
    /// <c>verification:</c> at all, so both fell to the engine's model-free floor. A consumer calling
    /// <c>AddMemory().AddMemoryAnnotation()</c> got a registered policy that never ran, while the identical
    /// registration behind <c>AddMemoryEngine(…, e =&gt; e.UseGraph())</c> worked. Silent in both directions:
    /// nothing threw, recall still returned hits, and the only symptom was quality.</para>
    /// <para>That is <c>pitfalls.md</c> §DI/config's "a documented option that isn't wired", and it mattered
    /// most for the seam whose own registration doc calls it <b>the single largest recall-quality lever the
    /// subsystem has</b>. The cause is the shape: two construction sites for one engine, with the argument
    /// list duplicated between them, so a parameter added to one is simply absent from the other.</para></summary>
    [Fact]
    public async Task The_one_line_AddMemory_path_honours_a_registered_annotation_and_verification_policy()
    {
        var annotation = new RecordingAnnotation();
        var verification = new RecordingVerification();

        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<IMemoryGraphStore>(new InMemoryMemoryGraphStore());
        services.AddSingleton<Lyntai.Memory.Annotation.IMemoryAnnotationPolicy>(annotation);
        services.AddSingleton<Lyntai.Memory.Verification.IMemoryVerificationPolicy>(verification);
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            b.AddMemory("m");             // the one-line path — NOT AddMemoryEngine(…, e => e.UseGraph())
        });
        using var sp = services.BuildServiceProvider();

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get("m/memory");
        await engine.RememberAsync(new MemoryWrite("t", "s", "the spouse is called Alice"));
        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "spouse"));

        Assert.NotEmpty(recall.Items);
        Assert.Equal(["the spouse is called Alice"], annotation.Seen);
        Assert.Equal(["spouse"], verification.Seen);
    }
}
