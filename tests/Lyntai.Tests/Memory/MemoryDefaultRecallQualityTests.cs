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
/// PINS the shipped memory defaults — <see cref="Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy"/> (ranking)
/// and <see cref="Lyntai.Memory.Forgetting.DsrRetrievability"/> (forgetting) — against one fixed corpus point,
/// so a change to either policy's arithmetic, or to how <see cref="GraphMemoryEngine"/> wires them together,
/// shows up here.
/// <para><b>This answers "did we break the default." It does NOT answer "which default is best"</b> — that is
/// <c>docs/memory-measurements.md</c> §5, which also owns the pinned numbers' provenance. A failure here means
/// the shipped default's own behaviour moved, nothing more: a regression signal, never the measurement.</para>
/// <para>Constructed the way <c>MemoryPolicySweep</c>'s "RRF+Live" arm is, for its two reasons: direct
/// construction (never DI), so no <see cref="Lyntai.Memory.Modulation.SalienceRetentionPolicy"/> enters the
/// retention collection as a third factor; and an undamped <see cref="PerWriteAgePolicy"/>, because a fast
/// in-process replay lands inside <see cref="Lyntai.Memory.Interference.BurstDampenedAgePolicy"/>'s wall-clock
/// burst window and would measure the damping instead (<see cref="MemoryDecaySimulationTests"/>). Ranking and
/// forgetting are passed EXPLICITLY although the bare constructor defaults to the same two, so the guard pins
/// the REGISTERED default even if the two ever split.</para>
/// <para><b>SQLite, not <c>InMemoryMemoryGraphStore</c></b>: the full-text path ranks by bm25 where the
/// in-process store reports a flat relevance, and SQLite is what the sweep measured on, so this guard and the
/// measurement it guards stay comparable.</para>
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
        var engine = new GraphMemoryEngine("default-guard", store, seams: new GraphMemorySeams
            {
                AgePolicies = [new PerWriteAgePolicy()],
                Retrievability = new DsrRetrievability(),
                Ranking = new ReciprocalRankFusionPolicy(),
            });

        var firstWrite = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var refToCorpusId = new Dictionary<string, string>(StringComparer.Ordinal);
        var qualities = new List<RecallQuality>();
        const int limit = 10;

        foreach (var step in corpus.Steps)
        {
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = (await engine.RememberAsync(w.Write)).Reference;
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

        // The pinned values and why they are what they are: docs/memory-measurements.md, result
        // reinforce-gain-zero-fixed-pin. A move in EITHER direction is a finding to explain before re-pinning —
        // removing the corpus's matchable filler once made both numbers WORSE, for honest reasons.
        //
        // The replay is deterministic (seeded corpus, single-writer SQLite, no wall clock), so the tolerance
        // only absorbs floating-point summation-order jitter across runtimes and SQLite builds.
        const double tolerance = 0.002;
        Assert.True(Math.Abs(missRate - 0.103448) < tolerance,
            $"MissRate moved: expected ~0.103448, got {missRate:F6}");
        Assert.True(Math.Abs(pollutionRate - 0.857931) < tolerance,
            $"PollutionRate moved: expected ~0.857931, got {pollutionRate:F6}");
    }
}
