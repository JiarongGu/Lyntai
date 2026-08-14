using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Modulation;
using Lyntai.Memory.Ranking;
using Lyntai.Memory.Salience;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;

namespace Lyntai.Tests.Memory;

/// <summary>Salience's contribution to RANK — the owner's ruling in <c>docs/DECISIONS.md</c> D45, correcting
/// D45's first round: salience means "this memory does not fade away" (decay resistance, plus
/// store ADMISSION priority — a salient candidate survives the candidate <c>LIMIT</c>, so it is there to be
/// found), not "first priority". Reordering a candidate ahead of a better textual match is a stronger,
/// separate claim, so <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/> defaults to 0 — off —
/// and a consumer opts in explicitly (now via an explicit <see cref="MultiplicativeRankingPolicy"/> passed
/// to the engine's <c>ranking</c> parameter — the knob moved off <see cref="GraphMemoryOptions"/> in the
/// wiring task, the assertions did not). <see cref="GraphMemorySalienceTests"/> already pins the decay half
/// and <c>MemoryGraphStoreContract</c> pins the admission half; these two pin the engine's RANK half,
/// default (off) and opt-in.
/// <para><b>Runs against SQLite, not InMemory, and that is load-bearing rather than a style choice.</b>
/// <see cref="Lyntai.Storage.InMemory.InMemoryMemoryGraphStore"/> reports <c>Relevance = 1</c> for every row
/// by contract, so there is no "better textual match" to lose to there — the opt-in fact would pass for the
/// wrong reason, deciding an otherwise-tied race rather than the one it names. Worse, the default-behaviour
/// fact would FAIL on InMemory: <see cref="SalienceRetentionPolicy"/> lengthens stability regardless of
/// <see cref="MultiplicativeRankingOptions.SalienceRankWeight"/>, so the salient entry would still win on
/// RETRIEVABILITY alone even with ranking off. The assertion is only meaningful where relevance actually
/// varies, which needs SQLite's bm25 ranking — and this is also the first <see cref="GraphMemoryEngine"/>
/// coverage ever to run against a SQL backend at all, closing a gap carried from Plan 1 (three of its six
/// defects lived in exactly this unexercised seam).</para></summary>
public sealed class GraphMemoryRankingTests : IDisposable
{
    private readonly TempDb _db = new();

    public void Dispose() => _db.Dispose();

    /// <summary>Reports a fixed salience so the test pins the RANK plumbing rather than the default
    /// salience policy's curve, which <see cref="SalienceTests"/> already covers.</summary>
    private sealed class FixedSaliencePolicy(double salience) : IMemorySaliencePolicy
    {
        // a fake's own bit, from the consumer range (32-62) — never None: fix round 2's provenance
        // validation rejects a policy declaring None, since every REAL, running policy has an identity.
        public MemorySalienceProvenance Provenance => (MemorySalienceProvenance)(1L << 32);

        public MemorySignals Signals(MemoryWrite write, in SalienceContext context) =>
            MemorySignals.Empty.With(MemorySignals.WellKnown.Salience, salience);
    }

    private GraphMemoryEngine Engine(IMemoryGraphStore store, IMemorySaliencePolicy saliencePolicy,
        IMemoryRankingPolicy? ranking = null) =>
        new("e", store, saliencePolicies: [saliencePolicy],
            policy: new ModulatedRetrievability(new DsrRetrievability(), [new SalienceRetentionPolicy()]),
            ranking: ranking);

    [Fact]
    public async Task An_explicitly_configured_rank_weight_can_outrank_a_better_textual_match()
    {
        // THE opt-in distortion — off by default (docs/DECISIONS.md D45: salience means "does not fade
        // away", not "first priority"). A consumer who explicitly wants salience to
        // reorder recall gets it by raising SalienceRankWeight above 0; asserting it here means a later
        // "fix" that silently disables the opt-in path fails loudly instead of quietly reverting it.
        //
        // The measurement this weight is chosen against (see
        // MultiplicativeRankingOptions.SalienceRankWeight's own doc for the general form): with only these
        // two writes in scope, SqliteMemoryGraphStore.SeedAsync normalizes Relevance by RANK POSITION —
        // `1 - i / rows.Count` — not by a smooth bm25 margin. With rows.Count == 2 that is EXACTLY 1.0 for
        // the better match and 0.5 for the other, a fixed 2x gap regardless of how close the underlying bm25
        // scores are, and retrievability (via the wired SalienceRetentionPolicy) contributes only another ~1% at
        // these ages — clearing the gap needs boost > ~2.02. At weight = 1.0 and this fact's own
        // FixedSaliencePolicy(4) — SalienceOptions.MaxSalience's default ceiling, the most a real
        // StructuralSaliencePolicy could ever report — the achievable boost is `1 + 1.0 * ln(4)` ≈ 2.386,
        // which clears it. A 2-candidate set is the WORST case for this rank-position normalization; a
        // larger one needs a far smaller weight (see TASKS.md Part 53).
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var ranking = new MultiplicativeRankingPolicy(new MultiplicativeRankingOptions { SalienceRankWeight = 1.0 });
        var salient = Engine(store, new FixedSaliencePolicy(4), ranking);
        var ordinary = Engine(store, new FixedSaliencePolicy(1), ranking);

        await salient.RememberAsync(new MemoryWrite("t", "s", "the user's spouse is Alice"));
        await ordinary.RememberAsync(new MemoryWrite("t", "s", "spouse spouse spouse booking notes"));

        var recalled = await salient.RecallAsync(new MemoryQuery("t", "s", "spouse", Limit: 10));

        Assert.Equal("the user's spouse is Alice", recalled.Items[0].Headline);
    }

    [Fact]
    public void The_shipped_rank_weight_default_is_exactly_zero()
    {
        // D45's ruling pinned as the CONSTANT, because the scenario fact below cannot see it.
        //
        // Worked from that fact's own numbers: its two candidates give relevance 1.0 / 0.5 and a
        // retrievability ratio of ≈ 0.991, so the ordinary entry keeps first place unless the salient one's
        // boost clears ≈ 2.02. At the shipped 0 the boost is 1 and it passes — but at D45's OVERTURNED 0.5
        // the boost is `1 + 0.5 × ln(4)` ≈ 1.693 and it passes too. The scenario only rejects a default above
        // ≈ 0.72, so a one-token revert of the owner's ruling would leave the whole suite green.
        //
        // Both facts are needed and neither replaces the other: this one rejects any non-zero default
        // outright, the scenario proves what a zero default actually DOES to a recall. The constant itself
        // now lives on MultiplicativeRankingOptions (MultiplicativeRankingPolicyTests pins the same value at
        // that home) — kept here too so this file's own pair of facts (constant + scenario) stays complete.
        Assert.Equal(0, new MultiplicativeRankingOptions().SalienceRankWeight);
    }

    [Fact]
    public async Task By_default_salience_does_not_reorder_a_better_textual_match()
    {
        // THE default behaviour: SalienceRankWeight's own default (0) leaves ranking untouched, so a
        // salient entry still competes on relevance like anything else — admission (Task 5) already
        // guarantees it reached the candidate set at all, which is what "does not fade away" means.
        //
        // `ranking: new MultiplicativeRankingPolicy()` passed EXPLICITLY (2026-08-11) — this fact's own
        // subject is `MultiplicativeRankingOptions.SalienceRankWeight`'s own default value, a MULTIPLICATIVE-
        // NAMED option with no ReciprocalRankFusionPolicy analogue (RRF's own `SalienceWeight` defaults to
        // `1`, not `0`, and works by rank fusion, not a logarithmic boost — a structurally different claim).
        // Leaving this at the bare constructor's own default (RRF, as of the same owner ruling that moved
        // MultiplicativeRankingPolicy off DI) would have kept this specific assertion passing BY COINCIDENCE
        // (measured: it does) rather than for the reason its own name and doc claim — the same judgement call
        // GraphMemoryRankingGoldenTests' own BuildEngine makes, for the identical reason.
        var store = new SqliteMemoryGraphStore(_db.Factory);
        var ranking = new MultiplicativeRankingPolicy();
        var salient = Engine(store, new FixedSaliencePolicy(4), ranking);
        var ordinary = Engine(store, new FixedSaliencePolicy(1), ranking);

        await salient.RememberAsync(new MemoryWrite("t", "s", "the user's spouse is Alice"));
        await ordinary.RememberAsync(new MemoryWrite("t", "s", "spouse spouse spouse booking notes"));

        var recalled = await salient.RecallAsync(new MemoryQuery("t", "s", "spouse", Limit: 10));

        Assert.Equal("spouse spouse spouse booking notes", recalled.Items[0].Headline);
    }
}
