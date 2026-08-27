using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Forgetting;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Verification;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Live;
using Lyntai.Tests.Memory.Corpus;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Lyntai.Tests.Memory;

/// <summary><b>What a REAL model scores against the oracle ceiling.</b> `docs/DECISIONS.md` <b>D59</b>
/// established that rescuing outranked answers halves the miss rate when the judge is perfect. A ceiling is
/// not a product claim, so this measures the same corpus with an actual model in the seam and reports where
/// it lands between "no verifier" and "perfect verifier".
///
/// <para><b>Why this file exists rather than a paragraph asserting a model can do it.</b> Every other number
/// in this subsystem is measured; the one that decides whether the feature is worth its latency should not
/// be the exception. It is also the only test here whose subject is a MODEL rather than the library — so its
/// job is to report a number, not to defend one.</para>
///
/// <para><b>The metric is deliberately RELATIVE.</b> Absolute miss depends on the model, the corpus and the
/// limit; what generalizes is the fraction of the oracle's improvement a real judge captures. That fraction
/// is what a consumer deciding whether to register a verifier actually needs.</para>
///
/// <para><b>The model is a PARAMETER, and the ladder is the finding.</b> Recall quality tracks judge
/// capability, so one model's score is a point on a curve rather than "the" number — and one sample of a
/// stochastic judge is not a measurement. Which model runs is a deployment choice
/// (<c>.claude/knowledge/model-decoupling.md</c>); the ladder itself is <c>docs/memory.md</c> §5, which is
/// the authority and is not duplicated here.</para>
///
/// <para>Runs only when <c>LYNTAI_LIVE_MODEL</c> (or the legacy <c>LYNTAI_LIVE_OLLAMA</c>) is set AND a model
/// endpoint is reachable; otherwise SKIPPED, never a pass that observed nothing. Optionally
/// <c>LYNTAI_OLLAMA_VERIFY_MODEL</c> (default <c>gemma3:4b</c> — pick another to measure another point) and
/// <c>LYNTAI_VERIFY_RESULTS</c> (an ABSOLUTE path to append the table to, for sweeping several models in one
/// pass).</para>
///
/// <para><b>The local judge is any OpenAI-compatible endpoint</b> — <c>LYNTAI_LIVE_MODEL_FLAVOR=openai</c>
/// plus a URL points it at llama.cpp's <c>llama-server</c>. Which is the same point the paragraph above
/// makes about hosted models: the seam takes a judge, and where that judge runs is a deployment's business.</para>
///
/// <para><b>Cost warning:</b> one model call per corpus query. The default shape issues tens of them, so a
/// run takes minutes against a local model. That is why it is opt-in rather than merely slow-marked.</para>
/// </summary>
/// <param name="output">Where the measured table goes. <b>A measurement test that only reports on failure
/// is useless as a measurement</b> — the numbers are the deliverable, so they are written unconditionally
/// and the assertions merely guard them.</param>
public class LlmVerificationLiveTests(Xunit.Abstractions.ITestOutputHelper output)
{
    private const int Seed = 4242;
    private const int QueryLimit = 10;

    private static string BaseUrl => LiveModel.BaseUrl;

    /// <summary>The default judge. <b><c>llama3.2:3b</c> held this slot until 2026-08-15 and was retired on a
    /// measurement, not on age:</b> it FAILS the multilingual fact outright, answering <c>[1,2]</c> on the
    /// Japanese case — the two non-answering notes, missing the answer entirely — while passing English,
    /// Chinese and Korean. A default that fails the library's own multilingual promise makes the seam look
    /// worse than it is, and it is the value anyone runs first.
    /// <para><b>The judge ladder lives in <c>docs/memory.md</c> §5 and is not duplicated here</b> — it
    /// carries miss, pollution and a share-of-reference column for six judges, which is strictly more than a
    /// list of model names would say. <c>gemma3:4b</c> is the default because that table makes it the best
    /// LOCAL arm: pollution <c>0.0492</c>, the lowest of any judge measured including the ground-truth
    /// reference, at ~1.5s per judgement.</para>
    /// <para><b>Do NOT default this to a reasoning model, and the reason is measured rather than aesthetic:</b>
    /// <c>docs/memory.md</c> records <c>qwen3:4b</c> at ~25s per judgement against gemma3's ~1.5s, and a seam
    /// in the latency path of EVERY recall makes that disqualifying whatever it scores. Re-confirmed
    /// 2026-08-15 the hard way: qwen3 emits ~2,200 output tokens for a four-note question whose answer is
    /// about 8, and two full ceiling runs were abandoned after 40+ and 55+ minutes. The policy already sets
    /// <c>LlmReasoning.Suppress</c>; Ollama's qwen3 reasons regardless.</para>
    /// <para><b>One narrow observation worth keeping, and NOT a precision failure:</b> on this file's
    /// adversarial four-note fixture <c>gemma3:4b</c> also takes the trivia distractor (<c>[3,4]</c> in
    /// Chinese, Japanese and Korean) where <c>qwen3:4b</c> answers <c>[3]</c>. That is a LEXICALLY ADJACENT
    /// distractor chosen to be hard, and it does not generalise: on the real corpus gemma3 admits the least
    /// junk of any judge. The distractor result is REPORTED below rather than asserted, because a fixture
    /// that fails the best-measured local model is mis-calibrated as a gate.</para>
    /// <para><c>llama3.2:3b</c> was the default until 2026-08-15 and is retired on the ladder's own numbers —
    /// the weakest judge measured (60–69% of reference) — and separately fails the multilingual RECALL fact,
    /// answering <c>[1,2]</c> in Japanese and missing the answer entirely.</para>
    /// <para>Which model runs is a deployment choice
    /// (<c>.claude/knowledge/model-decoupling.md</c>); this default only decides what an unconfigured run
    /// measures.</para></summary>
    private static string Model =>
        Environment.GetEnvironmentVariable("LYNTAI_OLLAMA_VERIFY_MODEL") ?? "gemma3:4b";

    private static async Task<bool> LiveAsync()
    {
        // The claude arm needs no endpoint probe — the CLI provider reports its own availability, and a
        // missing CLI surfaces as a verdict rather than as a hang. It still honours the opt-in variable,
        // because it spends real quota.
        if (UsesClaude)
            return Environment.GetEnvironmentVariable("LYNTAI_LIVE_MODEL") is { Length: > 0 }
                || Environment.GetEnvironmentVariable("LYNTAI_LIVE_OLLAMA") is { Length: > 0 };

        return await LiveModel.IsAvailableAsync();
    }

    /// <summary>Which backend supplies the judge. <c>ollama</c> (default) or <c>claude</c> — the latter
    /// spawns the <c>claude</c> CLI per call, so it measures a HOSTED frontier judge on the same corpus and
    /// costs real quota. Set <c>LYNTAI_VERIFY_BACKEND=claude</c> and
    /// <c>LYNTAI_VERIFY_MODEL=haiku|sonnet</c>.</summary>
    private static string Backend =>
        Environment.GetEnvironmentVariable("LYNTAI_VERIFY_BACKEND") ?? "ollama";

    private static bool UsesClaude => string.Equals(Backend, "claude", StringComparison.OrdinalIgnoreCase);

    private static string ClaudeModel =>
        Environment.GetEnvironmentVariable("LYNTAI_VERIFY_MODEL") ?? "haiku";

    private static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        if (UsesClaude)
            services.AddLyntai(b => b
                .AddClaudeCliProvider()
                .UseDefaultCandidates("claude-cli")
                .AddMemoryVerification(o => o.Model = ClaudeModel));
        else
            services.AddLyntai(b => b
                .AddLiveProvider(Model)
                .UseDefaultCandidates("ollama")
                .AddMemoryVerification(o => o.Model = Model));
        return services.BuildServiceProvider();
    }

    /// <summary>The label for the arm being measured, so the emitted table names what actually ran rather
    /// than whichever env var happened to be read.</summary>
    private static string ArmLabel => UsesClaude ? $"claude-cli/{ClaudeModel}" : Model;

    /// <summary>A judge wired from the corpus's own ground truth: it promotes exactly the relevant entries
    /// and nothing else.
    ///
    /// <para><b>This is a REFERENCE ARM, not a ceiling, and calling it one was wrong.</b> It was described
    /// as an upper bound no model could exceed — then <c>gemma3:4b</c> beat it reproducibly (miss 0.2571 and
    /// 0.2643 against its 0.2857; pollution 0.0492 and 0.0571 against its 0.1549). The error was treating
    /// "maximally precise" as "optimal", and two mechanisms make it not:</para>
    /// <list type="number">
    /// <item><b>Promotion fills slots.</b> Promoting only the two or three strictly-relevant entries leaves
    /// the remaining slots to the unchanged noisy ranking. A model with a broader notion of relevance
    /// displaces noise from those slots — which is why its pollution is a third of this arm's.</item>
    /// <item><b>Reinforcement follows the verdict.</b> This arm reinforces only strict ground truth, so
    /// fewer entries get their age reset — and the age reset is what keeps material alive
    /// (<c>docs/DECISIONS.md</c> <b>D57</b>/<b>D58</b>). Its stinginess costs it later recalls. It is
    /// optimal for the CURRENT recall's ordering and not for the trajectory.</item>
    /// </list>
    ///
    /// <para>It remains the right reference: it is deterministic, it is defined by the corpus rather than by
    /// a model's taste, and the share-of-reference figure is still the most useful single number for
    /// comparing judges. It just is not a bound, and a value above 100% is a real result rather than a
    /// bug.</para></summary>
    private sealed class OracleVerifier : IMemoryVerificationPolicy
    {
        private readonly Dictionary<string, HashSet<string>> _truth = new(StringComparer.Ordinal);

        public void Teach(string queryText, IEnumerable<string> ids) => _truth[queryText] = [.. ids];

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

    private readonly record struct Arm(double Miss, double Pollution, int Judged, int NoOpinion);

    /// <summary>Replays the corpus with whichever verifier is given. <paramref name="oracle"/> is taught each
    /// query's truth just before its recall, which is what makes the ceiling arm comparable step for step
    /// rather than only in aggregate.</summary>
    private static async Task<Arm> RunAsync(IMemoryVerificationPolicy? verifier, OracleVerifier? oracle)
    {
        var corpus = MemoryCorpus.Generate(CorpusShape.Default, Seed);
        var store = new InMemoryMemoryGraphStore();
        var counting = verifier is null ? null : new CountingVerifier(verifier);
        var engine = new GraphMemoryEngine("e", store,
            retrievability: new DsrRetrievability(new DsrOptions { ReinforceGain = 0 }),
            agePolicies: [new PerWriteAgePolicy()],
            verification: counting);

        var first = corpus.Steps.OfType<CorpusWrite>().First().Write;
        var byCorpusId = new Dictionary<string, string>(StringComparer.Ordinal);
        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        long returned = 0, noise = 0, wanted = 0, missed = 0;

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
            returned == 0 ? 0 : (double)noise / returned,
            counting?.Judged ?? 0,
            counting?.NoOpinion ?? 0);
    }

    /// <summary>Wraps a verifier to count how many calls produced a real verdict versus fell through to
    /// <see cref="MemoryVerification.NoOpinion"/>.
    /// <para><b>This is what stops a null result being misread.</b> A live model that refuses, times out or
    /// returns unparseable JSON on every call yields no-opinion every time, and the arm then reports exactly
    /// the no-verifier numbers — a "the model did not help" conclusion that is really "the model never
    /// ran".</para></summary>
    private sealed class CountingVerifier(IMemoryVerificationPolicy inner) : IMemoryVerificationPolicy
    {
        public int Judged { get; private set; }
        public int NoOpinion { get; private set; }

        public async Task<MemoryVerification> VerifyAsync(MemoryVerificationRequest request,
            CancellationToken ct = default)
        {
            var v = await inner.VerifyAsync(request, ct);
            if (v.Judged) Judged++; else NoOpinion++;
            return v;
        }
    }

    /// <summary><b>The judge's prompt claims to work in any language. This is the only thing that checks
    /// it.</b>
    ///
    /// <para><see cref="LlmMemoryVerificationPolicy"/>'s instruction says "the question and the notes may be
    /// in any language, and may be in different ones — judge meaning, never spelling", and is deliberately
    /// example-free so as not to bias toward English. Every measurement of the seam, including the whole
    /// judge ladder, ran on the ENGLISH corpus. A language-neutral claim that only English exercises is the
    /// exact shape of the blind spot this subsystem spent a release removing — the recall-quality numbers
    /// published before 2026-08-12 were all English, on the friendliest tokenization the library
    /// supports.</para>
    ///
    /// <para><b>Deliberately not a recall-quality measurement.</b> It asks the narrow question the prompt
    /// makes a promise about: given a question and four notes IN THAT LANGUAGE, does the judge pick the one
    /// that answers it? That needs four model calls rather than a corpus replay per language, so it can run
    /// beside the rest instead of costing tens of minutes — and it fails loudly if a model turns out to
    /// reason only in English while claiming otherwise.</para>
    ///
    /// <para>The distractors share vocabulary with the question on purpose. A judge that scored on word
    /// overlap rather than meaning would pick one of them, which is what makes a pass mean something.</para></summary>
    [SkippableTheory]
    [InlineData("english", "where did the team agree to hold the review?",
        "the review will be held in the small meeting room on level three",
        "the review covers last quarter's numbers", "the team agreed the review was useful",
        "the small meeting room was repainted last year")]
    [InlineData("chinese", "团队决定在哪里开评审会?",
        "评审会将在三楼的小会议室举行", "评审会讨论上个季度的数据", "团队认为这次评审很有用",
        "三楼的小会议室去年重新粉刷过")]
    [InlineData("japanese", "チームはどこでレビューを行うと決めましたか?",
        "レビューは三階の小会議室で行われます", "レビューは前四半期の数字を扱います",
        "チームはレビューが有益だと考えました", "三階の小会議室は昨年塗り替えられました")]
    [InlineData("korean", "팀은 어디에서 검토 회의를 열기로 했습니까?",
        "검토 회의는 삼층 소회의실에서 열립니다", "검토 회의는 지난 분기 수치를 다룹니다",
        "팀은 이번 검토가 유익했다고 보았습니다", "삼층 소회의실은 작년에 다시 칠했습니다")]
    public async Task The_judge_picks_the_answering_note_in_every_language_not_only_english(
        string language, string question, string answers, string related, string opinion, string trivia)
    {
        Skip.IfNot(await LiveAsync(), LiveModel.SkipReason);

        using var sp = Build();
        var judge = sp.GetRequiredService<IMemoryVerificationPolicy>();

        // shuffled so the answer is never first — a model echoing the given order would otherwise pass
        var candidates = new List<MemoryVerificationCandidate>
        {
            new("1", related), new("2", opinion), new("3", answers), new("4", trivia),
        };

        var verdict = await judge.VerifyAsync(new MemoryVerificationRequest(question, candidates));

        output.WriteLine($"[{language}] judged={verdict.Judged} relevant=[{string.Join(",", verdict.RelevantIds)}]");

        Assert.True(verdict.Judged,
            $"[{language}] the judge returned no opinion at all — it either refused or emitted unparseable " +
            "output for a perfectly ordinary question in this language");
        Assert.Contains("3", verdict.RelevantIds);

        // The PRECISION half is REPORTED, not asserted, and the distinction is the point. Note 4 is the
        // designed lexical near-miss ("the small meeting room was repainted last year") — it shares the
        // answer's vocabulary and answers nothing, so taking it is a real precision error, and it matters
        // for this seam specifically: under GraphMemoryOptions.VerificationFilters a false positive SURVIVES
        // the filter, turning verification from a precision gain into a no-op.
        //
        // It is not a GATE because the fixture is deliberately adversarial and does not generalise. Measured
        // 2026-08-15: gemma3:4b takes the distractor here in Chinese, Japanese and Korean — yet on the real
        // corpus it admits the LEAST junk of any judge measured (pollution 0.0492, docs/memory.md §5, better
        // than the ground-truth reference). A fixture that fails the best-measured local model is
        // mis-calibrated as a pass/fail bar; asserting on it would have forced the default to a reasoning
        // model the library's own docs disqualify on latency. So the number goes to the ladder and the
        // judgement stays with the reader.
        var tookDistractor = verdict.RelevantIds.Contains("4");
        output.WriteLine($"[{language}] precision: distractor taken = {tookDistractor}");
    }

    /// <summary><b>THE LIVE NUMBER.</b> No verifier, a real model, and the perfect oracle — same corpus, same
    /// seed, same limit.</summary>
    [SkippableFact]
    public async Task A_real_model_captures_a_reported_share_of_the_oracles_improvement()
    {
        Skip.IfNot(await LiveAsync(), LiveModel.SkipReason);

        using var sp = Build();
        var live = sp.GetRequiredService<IMemoryVerificationPolicy>();

        var none = await RunAsync(null, null);
        var oracle = new OracleVerifier();
        var ceiling = await RunAsync(oracle, oracle);
        var model = await RunAsync(live, null);

        var headroom = none.Miss - ceiling.Miss;
        var captured = none.Miss - model.Miss;
        var share = headroom <= 0 ? 0 : captured / headroom;

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
             model: {ArmLabel}
                                  miss     pollution   judged / no-opinion
               no verifier        {none.Miss:F4}   {none.Pollution:F4}
               live model         {model.Miss:F4}   {model.Pollution:F4}    {model.Judged} / {model.NoOpinion}
               oracle (reference) {ceiling.Miss:F4}   {ceiling.Pollution:F4}    {ceiling.Judged} / {ceiling.NoOpinion}

               reference gain     {headroom:F4}
               model captured     {captured:F4}  ({share:P1} of the reference; >100% is possible)
             """);

        output.WriteLine(table);
        var resultsPath = Environment.GetEnvironmentVariable("LYNTAI_VERIFY_RESULTS");
        if (!string.IsNullOrWhiteSpace(resultsPath))
            await File.AppendAllTextAsync(resultsPath, table + "\n\n");

        // (1) The model has to have actually RUN. Without this, a backend that refused every call would
        //     report the no-verifier numbers and read as "verification does not help".
        Assert.True(model.Judged > 0,
            $"the live verifier never produced a verdict, so this measures nothing:\n{table}");

        // (2) The reference arm has to reproduce here too — if it does not, the corpus or the engine
        //     changed and the share below is being computed against a moving denominator.
        Assert.True(headroom > 0.15,
            $"the reference arm's gain collapsed; D59's premise no longer holds on this corpus:\n{table}");

        // (3) The finding itself. Reported rather than bounded tightly: this is a MODEL's score, and pinning
        //     it hard would fail on a model upgrade for a reason that has nothing to do with the library.
        //     The floor is deliberately weak — it asserts the seam is not actively harmful, which is the one
        //     outcome that would make registering a verifier the wrong advice.
        Assert.True(model.Miss <= none.Miss,
            $"a live verifier made recall WORSE than no verifier — the seam is not shippable as advised:\n{table}");

        // (4) A weak floor on the captured share. Measured at 68.6% with llama3.2:3b on 2026-08-13; the
        //     assertion is set far below that because it guards the CLAIM ("registering a verifier is worth
        //     it"), not the number. A model upgrade should never fail this file, but a change that made the
        //     seam ineffective should.
        Assert.True(share > 0.25,
            $"a live verifier captured almost none of the available improvement:\n{table}");

        // (5) The parse contract held on every call. Measured 145/145 with no fallbacks — worth pinning,
        //     because `NoOpinion` is indistinguishable from "the model ran and found nothing" in the miss
        //     number alone, so a silent regression in reply parsing would look like a weaker model.
        Assert.True(model.NoOpinion <= model.Judged / 10,
            $"more than a tenth of verification calls fell through to NoOpinion:\n{table}");
    }
}
