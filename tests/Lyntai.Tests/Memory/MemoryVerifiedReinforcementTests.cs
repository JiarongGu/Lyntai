using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Verification;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Tests.Memory;

/// <summary><b>Can a correctness signal actually fix recall, or is this subsystem's ceiling lower than that?</b>
///
/// <para>Every dead end in the memory work traces to one missing observable: a recall reinforces what it
/// RETURNED, and nothing observes whether the return was right, so reinforcement is positive feedback on the
/// ranker's own prior. <see cref="IMemoryVerificationPolicy"/> is the seam that supplies the observation. The
/// question this file answers is whether supplying it is WORTH anything — measured before depending on a
/// model to provide it.</para>
///
/// <para><b>The instrument is an ORACLE, and that is the point.</b> <see cref="OracleVerifier"/> judges using
/// the corpus's own ground truth, so it is a perfect verifier — better than any model can be. That makes
/// this an <b>upper bound</b>: it says what verified reinforcement is worth if the judgement is free and
/// flawless. A real model lands somewhere below it. Measuring the ceiling first is what stops a subsystem
/// being built on a mechanism that could not have paid off even in principle — the mistake three earlier
/// rounds of constant-tuning made.</para>
///
/// <para><b>It is deliberately NOT a claim about any model.</b> Nothing here says an LLM can reach this;
/// what it establishes is whether the headroom exists at all, and how much of the shipped miss rate is
/// attributable to learning from unverified retrievals rather than to ranking or tokenization.</para></summary>
public sealed class MemoryVerifiedReinforcementTests
{
    private const int Seed = 4242;
    private const int QueryLimit = 10;

    /// <summary>A perfect verifier: it knows, per query, exactly which entries were the right answers.
    /// <para>Wired from the corpus's own <c>RelevantIds</c>, mapped through the engine-assigned ids. The
    /// query text is the key, which is safe here because the corpus's query texts are unique per
    /// step.</para></summary>
    private sealed class OracleVerifier : IMemoryVerificationPolicy
    {
        private readonly Dictionary<string, HashSet<string>> _truth = new(StringComparer.Ordinal);

        public void Teach(string queryText, IEnumerable<string> relevantEngineIds) =>
            _truth[queryText] = [.. relevantEngineIds];

        public Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request,
            CancellationToken ct = default)
        {
            if (!_truth.TryGetValue(request.Query, out var relevant))
                return Task.FromResult(MemoryVerification.NoOpinion);

            var hits = request.Candidates.Select(c => c.Id).Where(relevant.Contains).ToList();
            return Task.FromResult(hits.Count == 0
                ? MemoryVerification.NothingRelevant
                : new MemoryVerification(hits));
        }
    }

    private readonly record struct Arm(double Miss, double Pollution);

    private static async Task<Arm> RunAsync(bool verified, double gain, int? depth = null)
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default, Seed);
        var store = new InMemoryMemoryGraphStore();
        var oracle = verified ? new OracleVerifier() : null;
        var engine = new GraphMemoryEngine("e", store,
            policy: new DsrRetrievability(new DsrOptions { ReinforceGain = gain }),
            agePolicies: [new PerWriteAgePolicy()],
            options: new GraphMemoryOptions { VerificationDepth = depth },
            verification: oracle);

        var first = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var byCorpusId = new Dictionary<string, string>(StringComparer.Ordinal);
        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        long returned = 0, noise = 0, wanted = 0, missed = 0;

        foreach (var step in corpus.Steps)
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = await engine.RememberAsync(w.Write);
                    var corpusId = w.Write.Content.Split(' ')[1];
                    byCorpusId[corpusId] = memRef.Id;
                    byRef[memRef.Id] = corpusId;
                    break;

                case CorpusQuery q:
                    // teach the oracle THIS query's truth, translated into engine ids, immediately before
                    // the recall — the entries exist by now, which is what makes the mapping possible
                    oracle?.Teach(q.Text,
                        q.RelevantIds.Where(byCorpusId.ContainsKey).Select(id => byCorpusId[id]));

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
            returned == 0 ? 0 : (double)noise / returned);
    }

    /// <summary><b>THE CEILING MEASUREMENT.</b> Unverified against oracle-verified reinforcement, in both
    /// growth settings.</summary>
    [Fact]
    public async Task Verified_reinforcement_is_measured_against_a_perfect_judge_to_establish_the_ceiling()
    {
        var plainShipped = await RunAsync(verified: false, gain: 0);
        var oracleShipped = await RunAsync(verified: true, gain: 0);
        var plainGrowth = await RunAsync(verified: false, gain: 2.0);
        var oracleGrowth = await RunAsync(verified: true, gain: 2.0);

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
                                    miss              pollution
               shipped (growth 0)   {plainShipped.Miss:F4}            {plainShipped.Pollution:F4}
               + oracle verify      {oracleShipped.Miss:F4}            {oracleShipped.Pollution:F4}
               delta                {oracleShipped.Miss - plainShipped.Miss:+0.0000;-0.0000}           {oracleShipped.Pollution - plainShipped.Pollution:+0.0000;-0.0000}

               growth 2.0           {plainGrowth.Miss:F4}            {plainGrowth.Pollution:F4}
               + oracle verify      {oracleGrowth.Miss:F4}            {oracleGrowth.Pollution:F4}
               delta                {oracleGrowth.Miss - plainGrowth.Miss:+0.0000;-0.0000}           {oracleGrowth.Pollution - plainGrowth.Pollution:+0.0000;-0.0000}
             """);

        // A perfect judge helps, in both growth settings — but note how MODEST it is at this depth, which is
        // the finding: at depth == limit a verifier can only observe the loss, not undo it. The sweep below
        // is where the real value shows up.
        Assert.True(oracleShipped.Miss < plainShipped.Miss, table);
        Assert.True(oracleGrowth.Miss < plainGrowth.Miss, table);
        Assert.True(oracleShipped.Pollution < plainShipped.Pollution, table);
    }

    /// <summary><b>THE DEPTH SWEEP — what rescuing an outranked answer is worth.</b> Same perfect oracle,
    /// varying only how far down the ranking it is allowed to look. Depth equal to the limit is
    /// observe-only; beyond it, a judged-relevant candidate is promoted past the cut.</summary>
    [Fact]
    public async Task Verification_depth_sweep_shows_what_rescuing_an_outranked_answer_is_worth()
    {
        var plain = await RunAsync(verified: false, gain: 0);
        var arms = new Dictionary<int, Arm>();
        var rows = new List<string>();
        foreach (var depth in (int[])[10, 20, 40, 80, 5000])
        {
            var arm = await RunAsync(verified: true, gain: 0, depth: depth);
            arms[depth] = arm;
            rows.Add(string.Create(CultureInfo.InvariantCulture,
                $"   depth {depth,5}    {arm.Miss:F4}   {arm.Pollution:F4}   {arm.Miss - plain.Miss:+0.0000;-0.0000}"));
        }

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
                              miss     pollution  delta-miss
               no verifier    {plain.Miss:F4}   {plain.Pollution:F4}
             {string.Join('\n', rows)}
             """);

        // (1) Depth is what makes verification worth anything. Observe-only (depth == limit) recovers a
        //     fraction of what rescuing does — this is the fact that set the shipped default.
        Assert.True(arms[40].Miss < arms[10].Miss, table);
        Assert.True(plain.Miss - arms[40].Miss > 2 * (plain.Miss - arms[10].Miss),
            $"rescuing should be worth more than twice observing, or the depth default is not earned:\n{table}");

        // (2) It SATURATES, which is why the default is a small multiple of the limit rather than "all of
        //     them". Looking deeper costs judgement tokens and buys nothing past this point.
        Assert.Equal(arms[40].Miss, arms[80].Miss, precision: 10);
        Assert.Equal(arms[40].Miss, arms[5000].Miss, precision: 10);

        // (3) The shipped default factor lands ON the saturation point rather than short of it.
        Assert.Equal(40, QueryLimit * GraphMemoryOptions.DefaultVerificationDepthFactor);

        // (4) Pollution falls too, so this is not a miss/pollution trade being reported as a win.
        Assert.True(arms[40].Pollution < plain.Pollution, table);
    }

    /// <summary><b>Is the shipped MODEL-FREE ranker the problem, or is the task simply hard without
    /// semantics?</b> The decomposition below says 100% of misses are ranking failures; this asks whether
    /// swapping the shipped ranking policy moves them, because a consumer with no model registered gets
    /// whichever of these is the default and nothing else.</summary>
    [Fact]
    public async Task Whether_the_shipped_ranking_policy_is_what_loses_the_reachable_answers()
    {
        var rrf = await RankingArm(new Lyntai.Memory.Ranking.ReciprocalRankFusionPolicy());
        var mult = await RankingArm(new Lyntai.Memory.Ranking.MultiplicativeRankingPolicy());

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
                                            miss     pollution
               ReciprocalRankFusion (default)  {rrf.Miss:F4}   {rrf.Pollution:F4}
               Multiplicative                  {mult.Miss:F4}   {mult.Pollution:F4}
             """);

        // THE FINDING: the two shipped model-free policies are INDISTINGUISHABLE here — same miss, same
        // pollution, to ten places. They rank a set of ≥40 candidates (the depth sweep proves the set is at
        // least that large, since depths 20 and 40 differ), and both pick the same wrong ten.
        //
        // That is why the verification seam exists rather than another ranking formula: model-free policy
        // choice has no headroom left on this corpus, so "swap the ranker" is not an available fix. It also
        // bounds what D49's RRF-over-Multiplicative ruling claims — that was measured on `topical` in the
        // sweep, and it does not reproduce as a difference here.
        Assert.Equal(rrf.Miss, mult.Miss, precision: 10);
        Assert.Equal(rrf.Pollution, mult.Pollution, precision: 10);
        Assert.True(rrf.Miss > 0.4, $"if this ever drops, the premise for verification changed:\n{table}");

        async Task<Arm> RankingArm(Lyntai.Memory.Ranking.IMemoryRankingPolicy ranking)
        {
            var corpus = MemoryCorpus.Generate(CorpusShape.Default, Seed);
            var store = new InMemoryMemoryGraphStore();
            var engine = new GraphMemoryEngine("e", store,
                policy: new DsrRetrievability(new DsrOptions { ReinforceGain = 0 }),
                agePolicies: [new PerWriteAgePolicy()],
                ranking: ranking);
            var first = corpus.Steps.OfType<CorpusWrite>().First().Write;
            var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
            long returned = 0, noise = 0, wanted = 0, missed = 0;

            foreach (var step in corpus.Steps)
                switch (step)
                {
                    case CorpusWrite w:
                        var memRef = await engine.RememberAsync(w.Write);
                        byRef[memRef.Id] = w.Write.Content.Split(' ')[1];
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

            return new Arm(wanted == 0 ? 0 : (double)missed / wanted,
                returned == 0 ? 0 : (double)noise / returned);
        }
    }

    /// <summary><b>The review log can now contain a FAILURE, which is what `docs/DECISIONS.md` D51 called
    /// structurally impossible.</b>
    ///
    /// <para>D51 gave two reasons parameter fitting could never work here: the grade is a deterministic
    /// function of the model's own prediction, and <i>the log can only ever contain successes</i> — because
    /// a row was written only where a touch happened, and only reinforced entries are touched. A verifier
    /// defeats the first by judging from outside the curve. The second needed a code change, not a policy:
    /// the log write is now decoupled from the touch, so an entry the judge REJECTED is recorded with
    /// <c>Verified = false</c> while never being reinforced.</para>
    ///
    /// <para>Asserted on all three counts, because any one alone is satisfiable the wrong way: a rejected
    /// entry must be LOGGED, must be logged as <c>false</c>, and must NOT have been touched.</para></summary>
    [Fact]
    public async Task A_rejected_entry_is_logged_as_a_failure_and_is_not_reinforced()
    {
        var store = new InMemoryMemoryGraphStore();
        var oracle = new OracleVerifier();
        var engine = new GraphMemoryEngine("e", store,
            policy: new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 }),
            agePolicies: [new PerWriteAgePolicy()],
            verification: oracle);

        var keep = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));
        var drop = await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy rota changes monthly"));
        for (var i = 0; i < 6; i++)
            await engine.RememberAsync(new MemoryWrite("t", "s", $"unrelated filler entry number {i}"));

        // the judge accepts one of the two matching entries and rejects the other
        oracle.Teach("deploy", [keep.Id]);

        var dropId = long.Parse(drop.Id, CultureInfo.InvariantCulture);
        var beforeDrop = (await store.GetAsync("e", dropId))!;

        var recall = await engine.RecallAsync(new MemoryQuery("t", "s", "deploy", Limit: 10));
        Assert.Contains(recall.Items, i => i.Reference.Id == drop.Id);   // it WAS returned...

        var reviews = await store.ReviewsAsync("e");
        var keepId = long.Parse(keep.Id, CultureInfo.InvariantCulture);
        var dropRow = Assert.Single(reviews, r => r.NodeId == dropId);
        var keepRow = Assert.Single(reviews, r => r.NodeId == keepId);

        Assert.False(dropRow.Verified);        // ...and logged as a FAILURE — the observation D51 lacked
        Assert.True(keepRow.Verified);

        // ...while never being reinforced: its age did not reset and its stability did not grow.
        var afterDrop = (await store.GetAsync("e", dropId))!;
        Assert.Equal(beforeDrop.OrdinalAge, afterDrop.OrdinalAge, precision: 9);
        Assert.Equal(beforeDrop.Stability, afterDrop.Stability, precision: 9);
    }

    /// <summary>With NO verifier registered, every logged row carries <c>Verified = null</c> — never
    /// <c>false</c>.
    /// <para><b>The distinction is the whole point.</b> Collapsing "no judgement" into "judged irrelevant"
    /// would turn every deployment without a verifier into a corpus of fabricated failures — worse than
    /// having no outcome column at all, because it would look like data.</para></summary>
    [Fact]
    public async Task Without_a_verifier_every_logged_row_is_unjudged_rather_than_failed()
    {
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store,
            policy: new DsrRetrievability(new DsrOptions { ReinforceGain = 2.0 }),
            agePolicies: [new PerWriteAgePolicy()]);

        await engine.RememberAsync(new MemoryWrite("t", "s", "the deploy pipeline needs approval"));
        Assert.NotEmpty((await engine.RecallAsync(new MemoryQuery("t", "s", "deploy", Limit: 10))).Items);

        var reviews = await store.ReviewsAsync("e");
        Assert.NotEmpty(reviews);
        Assert.All(reviews, r => Assert.Null(r.Verified));
    }

    /// <summary><b>WHERE THE MISSES ACTUALLY GO.</b> The ceiling measurement above says a perfect judge only
    /// recovers ~0.09 of a ~0.54 miss rate, so learning is not the binding constraint. This decomposes the
    /// remainder into the two places an answer can be lost, which is what decides where a model would have
    /// to be applied to matter:
    ///
    /// <list type="number">
    /// <item><b>Never a candidate</b> — the entry was in the store and the query did not reach it at all.
    /// That is retrieval: tokenization, query wording, or the absence of a semantic route. No amount of
    /// ranking or learning can recover it.</item>
    /// <item><b>A candidate but cut</b> — the entry was reachable and something ranked it below the limit.
    /// That is ranking, and it is what a reranker (model or otherwise) can fix.</item>
    /// </list>
    ///
    /// <para>Measured externally rather than by reaching into the engine: the same query is replayed at a
    /// very large limit, so anything that comes back was reachable and anything that does not was never a
    /// candidate. That keeps the diagnostic honest about the SHIPPED path rather than a private one.</para></summary>
    [Fact]
    public async Task Where_the_misses_go_decomposed_into_unreachable_versus_merely_outranked()
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default, Seed);
        var store = new InMemoryMemoryGraphStore();
        var engine = new GraphMemoryEngine("e", store,
            policy: new DsrRetrievability(new DsrOptions { ReinforceGain = 0 }),
            agePolicies: [new PerWriteAgePolicy()]);

        var first = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        long wanted = 0, missedAtLimit = 0, unreachable = 0;

        foreach (var step in corpus.Steps)
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = await engine.RememberAsync(w.Write);
                    byRef[memRef.Id] = w.Write.Content.Split(' ')[1];
                    break;

                case CorpusQuery q when q.RelevantIds.Count > 0:
                    var atLimit = await Ids(engine, first, q.Text, QueryLimit);
                    var wideOpen = await Ids(engine, first, q.Text, 5000);

                    foreach (var want in q.RelevantIds)
                    {
                        wanted++;
                        if (atLimit.Contains(want)) continue;
                        missedAtLimit++;
                        if (!wideOpen.Contains(want)) unreachable++;
                    }
                    break;
            }

        var missRate = (double)missedAtLimit / wanted;
        var unreachableShare = (double)unreachable / missedAtLimit;

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
             relevant entries wanted:            {wanted}
             missed at limit {QueryLimit}:                  {missedAtLimit}  ({missRate:P1})
               of those, NEVER a candidate:      {unreachable}  ({unreachableShare:P1} of misses)
               of those, reachable but outranked:{missedAtLimit - unreachable}  ({1 - unreachableShare:P1} of misses)
             """);

        // THE LOAD-BEARING FACT OF THIS FILE. Every miss is a ranking failure; none is a retrieval or
        // tokenization failure. That is what makes a reranking judge the right lever and rules out the
        // things a session would otherwise reach for first — a better tokenizer, more n-grams, a semantic
        // index — none of which can help with an answer that was already in the candidate set.
        Assert.True(missedAtLimit > 0, $"nothing was missed, so this decomposes nothing:\n{table}");
        Assert.Equal(0, unreachable);

        async Task<HashSet<string>> Ids(GraphMemoryEngine e, MemoryWrite f, string text, int limit)
        {
            var r = await e.RecallAsync(new MemoryQuery(f.TaskKey, f.Scope, text, Limit: limit));
            return [.. r.Items.Select(i => byRef.TryGetValue(i.Reference.Id, out var id) ? id : i.Reference.Id)];
        }
    }
}
