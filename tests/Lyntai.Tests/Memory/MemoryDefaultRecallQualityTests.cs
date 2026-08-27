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
/// PINS today's shipped memory defaults — as of 3.0, <see cref="Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy"/>
/// (ranking) and <see cref="Lyntai.Memory.Forgetting.DsrRetrievability"/> (forgetting) — against
/// one fixed corpus point, so a change to either policy's arithmetic, or to how
/// <see cref="GraphMemoryEngine"/> wires them together by default, shows up here.
/// <para><b>RE-BASELINED A SECOND TIME, 2026-08-11 (owner ruling; see <c>docs/DECISIONS.md</c>): the RANKING
/// default changed, not the arithmetic.</b> <see cref="Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy"/> is
/// now the registered ranking policy (<c>MemoryEngineRegistration.AddMemoryEngine</c> TryAdds it), on the
/// strength of this library's own measurement (<c>local/superpowers/records/2026-08-09-memory-policy-measurement.md</c>, fsrs-
/// properly plan Task 4): RRF beat <see cref="Lyntai.Memory.Ranking.MultiplicativeRankingPolicy"/> on the
/// corpus's `topical` class in all six measured shapes, reproduced across two independent runs. This is the
/// SECOND legitimate re-baseline this guard has carried — the first (2026-08-10, the forgetting-curve
/// default) is preserved in the comment history below rather than erased, so the sequence of which default
/// moved, and when, and why, stays readable from this one file.</para>
/// <para><b>This answers "did we break the default." It does NOT answer "which default is best."</b> That
/// question was this library's own falsification pass (<c>local/superpowers/records/2026-08-09-memory-policy-measurement.md</c>)
/// for the forgetting curve, and the same document's Task 4 re-measurement for ranking — neither is this
/// file's job to re-decide. A pass here is not evidence either policy is correct; a failure here means the
/// shipped default's own behaviour moved, nothing more — read it as a regression signal, never as the
/// measurement.</para>
/// <para>Uses the same construction shape <c>MemoryPolicySweep</c>'s own "RRF+Live" arm uses, for the same
/// two reasons: direct <see cref="GraphMemoryEngine"/> construction (never <c>AddMemoryEngine</c>/DI) so no
/// <see cref="Lyntai.Memory.Modulation.SalienceRetentionPolicy"/> enters the retention policy collection and
/// confounds the default with an unrelated third factor; and an undamped <see cref="PerWriteAgePolicy"/>
/// rather than the engine's own default <see cref="Lyntai.Memory.Interference.BurstDampenedAgePolicy"/>,
/// because a fast in-process replay lands entirely inside that policy's wall-clock burst window and would
/// flatten the corpus's whole interference axis, measuring the damping instead of the defaults under test —
/// the identical reasoning <see cref="MemoryDecaySimulationTests"/>'s own class doc gives for the same
/// substitution. Both <c>ranking</c> and <c>policy</c> are passed EXPLICITLY below even though the bare
/// constructor's own defaults now agree with the DI-registered ones for both seams (no two-defaults split
/// survives for either domain as of this task) — kept explicit anyway, to pin INTENT: this guard is about
/// the REGISTERED default, not whichever policy the bare constructor happens to default to today, and
/// stating it explicitly means a future two-defaults split (should one ever reappear) cannot silently change
/// what this file is measuring out from under it.</para>
/// <para><b>SQLite, not <c>InMemoryMemoryGraphStore</c>.</b> The corpus's own query text
/// (<c>"item topic5 repeat0"</c>, <c>"what happened to critical3"</c>, …) is deliberately NOT a contiguous
/// substring of any entry's content, so it can only be found by TOKEN matching. <b>The original reason given
/// here — that <c>InMemoryMemoryGraphStore</c> matches by contiguous substring and therefore measured
/// MissRate ≈ 0.93 on this exact corpus point — stopped being true in 3.0</b>, when
/// <see cref="Lyntai.Storage.SearchTerms"/> made every backend split a query the same way; that number is
/// kept as history, not as a live claim. What still argues for SQLite is that it RANKS (trigram/bm25) where
/// the InMemory store has no relevance score to give, and that it is what the sweep itself measured on — so
/// this guard and the measurement it guards stay comparable. Uses <see cref="TempDb"/>, the same per-test
/// fixture every other SQLite integration test in this project uses — no new registry entry, no
/// project-reference cost (this file already lives in <c>Lyntai.Tests</c>, which already references
/// <c>Lyntai.Storage.Sqlite</c>).</para>
/// </summary>
public class MemoryDefaultRecallQualityTests
{
    // The sweep's own seed, reused here only so this test's corpus point is traceable to the same
    // measurement rather than an unrelated draw — not required for correctness, since any fixed seed pins a
    // reproducible corpus.
    private const int Seed = 12345;

    [Fact]
    public async Task Todays_shipped_defaults_hold_their_recall_quality_on_a_fixed_corpus_point()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default, Seed);
        using var db = new TempDb();
        var store = new SqliteMemoryGraphStore(db.Factory);
        var engine = new GraphMemoryEngine("default-guard", store, agePolicies: [new PerWriteAgePolicy()],
            retrievability: new DsrRetrievability(), ranking: new ReciprocalRankFusionPolicy());
        // ranking: ReciprocalRankFusionPolicy, the DI-REGISTERED default as of 3.0 (owner ruling, 2026-08-11)
        // — also the bare constructor's own default now, but passed explicitly anyway to pin INTENT.
        // retrievability: DsrRetrievability, the DI-REGISTERED default as of 3.0 (D49) — also the bare constructor's
        // own default now that HalfLifeRetrievability is deleted, but passed explicitly anyway to pin INTENT
        // (see the class doc's last paragraph).

        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var refToCorpusId = new Dictionary<string, string>(StringComparer.Ordinal);
        var qualities = new List<RecallQuality>();
        const int limit = 10;

        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = await engine.RememberAsync(w.Write);
                    refToCorpusId[memRef.Id] = MemoryCorpusTestAccess.IdOf(w.Write.Content);
                    break;

                case CorpusQuery q:
                    var recall = await engine.RecallAsync(
                        new MemoryQuery(firstWrite.TaskKey, firstWrite.Scope, q.Text, Limit: limit));
                    var recalledIds = recall.Items
                        .Select(i => refToCorpusId.TryGetValue(i.Reference.Id, out var cid) ? cid : i.Reference.Id)
                        .ToList();
                    // q.SupportNeeded, not the default: a frequency query's answer is n OF its relevant set,
                    // and dropping it here would silently score that class by strict all-of instead.
                    qualities.Add(RecallQuality.Measure(recalledIds, q.RelevantIds, limit, q.SupportNeeded));
                    break;
            }
        }

        var missRate = qualities.Average(r => r.MissRate);
        var pollutionRate = qualities.Average(r => r.PollutionRate);

        // RE-PINNED THREE TIMES — history kept, not erased, so the sequence of which default moved, and
        // when, and why, stays readable from this one file.
        //
        // First 2026-08-10 (DSR-default falsification plan, Task 1 / TASKS.md Part 55): the corpus generator
        // (MemoryCorpus.Generate) was retargeted into the age/S band where the two shipped forgetting curves
        // actually diverge (1.5-5, up from ~1.2) — that pass measured, under the THEN-shipped default
        // (Multiplicative+HalfLife): miss=0.337931, pollution=0.881379.
        //
        // Second, the same day (Task 4): the FORGETTING default changed — DsrRetrievability became the
        // registered forgetting curve (D49). Measured on the identical construction, curve swapped, RANKING
        // STILL Multiplicative: miss=0.337931 (UNCHANGED — the ranking assertion alone cannot see a curve
        // swap on this shape), pollution=0.875172 (was 0.881379 under HalfLife).
        //
        // Third, 2026-08-11 (owner ruling, this task): the RANKING default changed — ReciprocalRankFusionPolicy
        // became the registered ranking policy, on the strength of local/superpowers/records/2026-08-09-memory-policy-measurement.md's
        // own Task 4 re-measurement. Measured on the IDENTICAL construction, ranking swapped, forgetting STILL
        // Dsr: miss=0.179310 (was 0.337931 under Multiplicative — a ~0.159 swing, MEASURED here directly, not
        // fitted; this exact number was already independently derived and left in a comment by whoever wrote
        // the second re-baseline above, and this run reproduced it bit-for-bit), pollution=0.865517 (was
        // 0.875172 — a further ~0.0097 move in the same direction PollutionRate already moved under the
        // forgetting-curve swap).
        //
        // FOURTH, 2026-08-11 (TASKS.md Part 56, the corpus-filler item) — and the ONLY re-pin so far caused by
        // the CORPUS rather than by a shipped default. No policy changed: ranking is still
        // ReciprocalRankFusionPolicy and forgetting is still DsrRetrievability, byte-for-byte. What changed is
        // MemoryCorpus.WriteFiller, whose padding used to begin "item filler{n} …" and therefore shared the
        // token `item` with every real entry AND with almost every query. FtsQuery.Build OR-joins a query's
        // tokens, so every freshly-written filler was a live candidate for every query in the corpus. Filler
        // now begins "padding filler{n} …" and can match nothing (pinned by
        // MemoryCorpusTests.No_filler_entry_can_match_any_query_in_the_corpus).
        //
        //   miss=0.234483 (was 0.179310 — WORSE by ~0.055)
        //   pollution=0.871034 (was 0.865517 — WORSE by ~0.0055)
        //
        // BOTH MOVED IN THE "WORSE" DIRECTION, WHICH IS NOT WHAT REMOVING A COMPETITOR SOUNDS LIKE IT SHOULD
        // DO, and that is worth stating rather than smoothing over. The corpus TIMELINE is structurally
        // unchanged — identical write count, identical ordering, identical interposition, so every
        // discriminating-band guard in MemoryCorpusTests still passes and the age axis (PerWriteAgePolicy here)
        // cannot have moved. The only thing that changed is WHICH entries are eligible to be RETURNED.
        //
        // The mechanism is a HYPOTHESIS, labelled as one because it has not been isolated: recall reinforces
        // what it returns. While filler was matchable it occupied much of every page and absorbed that
        // reinforcement harmlessly, since it is relevant to nothing and queried by nothing. With filler
        // ineligible, those slots now go to REAL but irrelevant entries (noise, and other classes' targets),
        // which are therefore reinforced far more often, grow more retrievable, and compete harder against the
        // genuinely relevant but decayed target on every later query. If that is right, the OLD numbers were
        // flattered by the scaffolding soaking up reinforcement, and these are the honest ones.
        //
        // Either way the instrument is now measuring the population it DECLARES rather than partly measuring
        // its own padding, so these numbers replace the old ones rather than sitting beside them. The absolute
        // values are not comparable across this boundary; only measurements taken on the same side of it are.
        //
        // Tolerance reasoning: this replay is fully deterministic (seeded corpus generator, a single-writer
        // SQLite db, an undamped age policy with no wall-clock dependency), so a naive choice would be exact
        // equality. A small nonzero tolerance is used instead, to absorb floating-point summation-order
        // jitter across .NET runtimes/architectures/SQLite builds that this test does not control for, which
        // exact equality would be fragile against. The tolerance stays at 0.002 (set at the second re-pin) —
        // both new values sit far outside it relative to their OLD pins, which is exactly the point: this
        // guard is meant to fail loudly on a change it can see, and it has now done so three times.
        // FIFTH, 2026-08-12 (docs/DECISIONS.md D54) — DsrOptions.ReinforceGain now defaults to 0, so a
        // recall still RESETS an entry's age but no longer grows its half-life. This is the largest single
        // movement this pin has ever recorded, and it is an IMPROVEMENT rather than a re-baseline:
        //
        //   miss=0.103448      (was 0.234483 — BETTER by ~0.131, a 56% reduction)
        //   pollution=0.857931 (was 0.871034 — BETTER by ~0.013)
        //
        // BOTH moved in the BETTER direction, which is worth stating explicitly because the fourth re-pin
        // above is a standing reminder that this pin can move the "wrong" way for honest reasons. It did
        // not this time: fewer misses AND less pollution, from one default.
        //
        // It is also the outcome five separate studies predicted before this default changed, which is the
        // reason it is being adopted rather than investigated further: retrieval-driven stability growth
        // was measured harmful on every corpus shape and both metrics, and a capped variant and a
        // non-compounding one (stability from recall COUNT, unable to compound by construction) were both
        // tried and both lost to simply not growing. What a recall is worth comes from the age reset, which
        // expires; a permanent half-life increase banks the ranking policy's own errors instead.
        const double tolerance = 0.002;
        Assert.True(Math.Abs(missRate - 0.103448) < tolerance,
            $"MissRate moved: expected ~0.103448, got {missRate:F6}");
        Assert.True(Math.Abs(pollutionRate - 0.857931) < tolerance,
            $"PollutionRate moved: expected ~0.857931, got {pollutionRate:F6}");
    }
}
