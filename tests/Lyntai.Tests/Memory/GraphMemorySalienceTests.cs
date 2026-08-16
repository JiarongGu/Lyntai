using Lyntai.Embeddings;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Storage.InMemory;

namespace Lyntai.Tests.Memory;

/// <summary>Salience end to end over the InMemory store: judged at write, persisted with the node,
/// read back into the decay state, and consumed by the retention policy.</summary>
public class GraphMemorySalienceTests
{
    /// <summary>Reports a fixed salience so the test pins PLUMBING rather than the default salience policy's
    /// curve, which <see cref="SalienceTests"/> already covers.</summary>
    private sealed class FixedSaliencePolicy(double salience) : IMemorySaliencePolicy
    {
        // a fake's own bit, from the consumer range (32-62) — never None: fix round 2's provenance
        // validation rejects a policy declaring None, since every REAL, running policy has an identity.
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context) =>
            MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, salience);
    }

    /// <summary>Always declines to judge — the salience policy a re-remember sees when the shared search finds
    /// nothing to compare against but this write's own prior self.</summary>
    private sealed class EmptySaliencePolicy : IMemorySaliencePolicy
    {
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 33);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context) => MemorySignals.Empty;
    }

    private sealed class ThrowingSaliencePolicy : IMemorySaliencePolicy
    {
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 34);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context) =>
            throw new InvalidOperationException("the salience policy is broken");
    }

    private sealed class ThrowingEmbedder : IEmbedder
    {
        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("embedding endpoint is down");
    }

    private static GraphMemoryEngine Engine(IMemoryGraphStore store, IMemorySaliencePolicy? saliencePolicy) =>
        new("e", store, saliencePolicies: saliencePolicy is null ? null : [saliencePolicy],
            retrievability: new ModulatedRetrievability(new DsrRetrievability(), [new SalienceRetentionPolicy()]));

    [Fact]
    public async Task A_judged_salience_is_stored_with_the_node_and_read_back()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = Engine(store, new FixedSaliencePolicy(3));

        await engine.RememberAsync(new MemoryWrite("t", "s", "a fact worth keeping"));

        var seeded = await store.SeedAsync("e", "t", "s", null, 10);
        Assert.Equal(3, Assert.Single(seeded).Signals.Get(MemorySignals.WellKnown.Salience));
    }

    [Fact]
    public async Task A_salient_entry_outranks_an_ordinary_one_despite_being_the_older_write()
    {
        // the whole point: same STORED stability, different salience → different retrievability. And the
        // asymmetry runs AGAINST the assertion, not for it — "the salient one" is written FIRST, so by the
        // time both are ranked it is one advance OLDER (more interfered-with) than "the ordinary one", which
        // is written second. Salience has to overcome that age handicap for this to pass, so a retention policy
        // that silently did nothing could not slip through by riding on a recency advantage it doesn't have.
        var store = new InMemoryMemoryGraphStore();
        var salient = Engine(store, new FixedSaliencePolicy(4));
        var ordinary = Engine(store, new FixedSaliencePolicy(1));

        await salient.RememberAsync(new MemoryWrite("t", "s", "the salient one"));
        await ordinary.RememberAsync(new MemoryWrite("t", "s", "the ordinary one"));
        for (var i = 0; i < 40; i++)
            await ordinary.RememberAsync(new MemoryWrite("t", "s", $"interference {i}"));

        var recalled = await salient.RecallAsync(new MemoryQuery("t", "s", Limit: 50));

        var high = recalled.Items.Single(i => i.Headline.Contains("the salient one"));
        var low = recalled.Items.Single(i => i.Headline.Contains("the ordinary one"));
        Assert.True(high.Retrievability > low.Retrievability,
            $"salient {high.Retrievability} should exceed ordinary {low.Retrievability}");
    }

    [Fact]
    public async Task With_no_embedder_the_default_salience_policy_records_nothing()
    {
        // the default path must be byte-identical to 2.5.0 decay behaviour for anyone who wires nothing —
        // a salience policy IS registered (the default), but with no embedder there is no novelty to judge
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store);

        await engine.RememberAsync(new MemoryWrite("t", "s", "an ordinary fact"));

        var seeded = await store.SeedAsync("e", "t", "s", null, 10);
        Assert.Equal(0, Assert.Single(seeded).Signals.Count);
    }

    [Fact]
    public async Task A_re_remember_with_no_fresh_judgement_keeps_the_previously_stored_salience()
    {
        // identical content is a REFRESH, not a new node — and the salience policy that sees the second write
        // is exactly the one most likely to decline (its own prior self is the only thing to compare against).
        // An empty bag from that second judgement must not blank the salience the FIRST write earned; a
        // re-remember is a REINFORCEMENT, and reinforcement erasing what it reinforces is the inverse of
        // intent.
        var store = new InMemoryMemoryGraphStore();
        var loud = Engine(store, new FixedSaliencePolicy(5));
        var quiet = Engine(store, new EmptySaliencePolicy());

        await loud.RememberAsync(new MemoryWrite("t", "s", "a fact worth keeping"));
        await quiet.RememberAsync(new MemoryWrite("t", "s", "a fact worth keeping")); // identical content

        var seeded = await store.SeedAsync("e", "t", "s", null, 10);
        Assert.Equal(5, Assert.Single(seeded).Signals.Get(MemorySignals.WellKnown.Salience));
    }

    [Fact]
    public async Task A_re_remember_WITH_a_fresh_judgement_replaces_the_stored_salience()
    {
        // The other half of preserve-on-empty. Preserving unconditionally would be its own bug: a fresh
        // judgement must be able to correct a stale one, or salience freezes at whatever the
        // first write happened to guess.
        var store = new InMemoryMemoryGraphStore();
        await Engine(store, new FixedSaliencePolicy(2)).RememberAsync(new MemoryWrite("t", "s", "a fact"));
        await Engine(store, new FixedSaliencePolicy(5)).RememberAsync(new MemoryWrite("t", "s", "a fact"));

        var node = Assert.Single(await store.SeedAsync("e", "t", "s", null, 10));
        Assert.Equal(5, node.Signals.Get(MemorySignals.WellKnown.Salience), 6);
    }

    [Fact]
    public async Task A_throwing_salience_policy_degrades_to_no_signals_rather_than_losing_the_write()
    {
        // best-effort, exactly like similarity enrichment: a broken salience policy must not fail the write
        var store = new InMemoryMemoryGraphStore();
        var engine = Engine(store, new ThrowingSaliencePolicy());

        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", "still stored"));

        Assert.NotNull(reference.Id);
        var seeded = await store.SeedAsync("e", "t", "s", null, 10);
        Assert.Equal(0, Assert.Single(seeded).Signals.Count);
    }

    [Fact]
    public async Task A_throwing_embedder_degrades_to_no_signals_rather_than_losing_the_write()
    {
        // the shared similarity search now feeds salience judgement too, so a broken embedder must degrade the
        // SAME way it already does for enrichment: the write still succeeds, and with no comparables the
        // (default) salience policy records nothing rather than the caller ever seeing the exception
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store,
            embedder: new ThrowingEmbedder(), vectors: new InMemoryVectorStore());

        var reference = await engine.RememberAsync(new MemoryWrite("t", "s", "still stored"));

        Assert.NotNull(reference.Id);
        var seeded = await store.SeedAsync("e", "t", "s", null, 10);
        Assert.Equal(0, Assert.Single(seeded).Signals.Count);
    }

    [Fact]
    public async Task A_non_finite_salience_cannot_black_out_recall_when_ranking_is_opted_into()
    {
        // THE worst failure mode the shared coercion (MemorySignals.Salience) closes, and the reason a bare
        // Math.Max(1, …) is not enough: Math.Max(1, NaN) is NaN by IEEE 754:2019, so `boost` and therefore
        // every candidate's `Rank` would be NaN — after which `best` and `floor` are NaN too, `Rank >= floor`
        // is FALSE for every candidate, and RecallAsync returns MemoryRecall.Empty. Not a mis-ordering: a
        // total, silent recall blackout, reachable through the public IMemorySaliencePolicy seam.
        //
        // InMemory, deliberately: it stores the bag VERBATIM, so the NaN actually reaches the engine. The SQL
        // backends drop a non-finite member in MemorySignalsJson.Serialize, so the engine would never see one
        // there and the test would pass for the wrong reason.
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store,
            saliencePolicies: [new FixedSaliencePolicy(double.NaN)],
            retrievability: new ModulatedRetrievability(new DsrRetrievability(), [new SalienceRetentionPolicy()]),
            ranking: new MultiplicativeRankingPolicy(
                new MultiplicativeRankingOptions { SalienceRankWeight = 1.0 }));

        await engine.RememberAsync(new MemoryWrite("t", "s", "a fact worth keeping"));
        await engine.RememberAsync(new MemoryWrite("t", "s", "another fact worth keeping"));

        var recalled = await engine.RecallAsync(new MemoryQuery("t", "s", Limit: 10));

        Assert.Equal(2, recalled.Items.Count);
    }
}
