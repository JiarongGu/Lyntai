using System.Globalization;
using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Memory.Interference;
using Lyntai.Memory.Salience;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Lyntai.Tests.Memory.Corpus;

namespace Lyntai.Tests.Memory;

/// <summary><b>Does the shipped salience policy preferentially preserve JUNK?</b> `TASKS.md` Part 53's open
/// concern, and the one memory question 3.0 had no instrument for.
///
/// <para><b>The concern, stated precisely.</b> <see cref="StructuralSaliencePolicy"/> is
/// <c>clamp(1 + NoveltyWeight * novelty, 1, MaxSalience)</c> — monotone in "how unlike anything already
/// stored". Random junk is maximally unlike everything. So on textually varied noise the mechanism that
/// decides "this does not fade away" should preferentially preserve junk AND grant it store-admission
/// priority, which is <c>PollutionRate</c> by definition. <see cref="SalienceOptions.MinimumComparables"/>
/// guards only the empty-engine case, not this one.</para>
///
/// <para><b>Why it was unfalsifiable until now, and it was not the reason first recorded.</b> The blocker
/// was written down as "needs an embedder" — half wrong. <see cref="FakeEmbedder"/> has existed all along.
/// The real blocker was the CORPUS: its noise shared one fixed skeleton, differing only by an id token and a
/// filler word, so under bag-of-words novelty the second noise entry onward reads as FAMILIAR. The corpus
/// modelled noise as <i>semantically irrelevant</i>; the hypothesis is about <i>textually diverse</i>. Those
/// are different properties and only one of them was expressible.
/// <see cref="CorpusNoiseKind.Diverse"/> is what closed that gap.</para>
///
/// <para><b>Salience was absent from EVERY measurement this repository has ever taken.</b> Not by oversight
/// in these tests — by assertion in the sweep's own control, which reflects over each constructed engine to
/// confirm the retention collection is empty. It is doubly invisible: novelty needs an
/// <see cref="Lyntai.Embeddings.IEmbedder"/> and no harness registered one. Meanwhile it ships ON: decay
/// resistance and admission priority are both default-on (only the rank boost is opt-in, <b>D45</b>). A
/// live retention dimension that no measurement exercised is exactly the shape of blind spot
/// <c>pitfalls.md</c> now warns about.</para>
///
/// <para><b>English only, and that is a real limit rather than an oversight.</b>
/// <see cref="FakeEmbedder"/> is a feature-hashed bag of WHITESPACE-SPLIT words, so on a spaceless script
/// every entry collapses to a single token, every entry is unique, and novelty pins at its maximum for
/// reasons that belong to the test double rather than to the policy. A CJK arm here would measure the
/// embedder, not the hypothesis. Answering it for CJK needs an embedder that segments.</para></summary>
public sealed class MemorySalienceInversionTests
{
    private const int Seed = 4242;
    private const int QueryLimit = 10;

    /// <summary>The three arms, and the middle one is the whole reason this is trustworthy.
    ///
    /// <para><b>Salience cannot be switched on alone.</b> Novelty comes from the engine's own similarity
    /// search, so a salience arm necessarily also registers an <see cref="Lyntai.Embeddings.IEmbedder"/> and
    /// an <see cref="Lyntai.Storage.IVectorStore"/> — and those change recall by themselves, because the
    /// vector search seeds candidates that compete for the same limited slots. Comparing
    /// <see cref="Off"/> against <see cref="Salience"/> therefore measures <i>embedder + salience</i> and
    /// attributes all of it to salience.</para>
    ///
    /// <para><b>The first version of this file had exactly that defect and reported the inversion as
    /// reproduced</b> (+0.1857 misses on diverse noise). <see cref="Embedder"/> is the arm that separates
    /// them: same embedder, same vector store, no salience policy — so <c>Embedder to Salience</c> isolates
    /// the one factor under test, the same one-factor-at-a-time discipline the language sweep is built on
    /// (<b>D55</b>).</para>
    /// </summary>
    private enum ArmKind
    {
        /// <summary>No embedder, no vector store, no salience — what every prior measurement here ran.</summary>
        Off,

        /// <summary>Embedder and vector store, no salience policy. The control.</summary>
        Embedder,

        /// <summary>Embedder, vector store AND the shipped salience policy.</summary>
        Salience,
    }

    /// <summary><b>Salience cannot be switched off by registering nothing</b> — <c>NormalizeSaliencePolicies</c>
    /// treats null-or-empty as "take the shipped default", exactly as the age seam does, so an empty list
    /// yields a live <see cref="StructuralSaliencePolicy"/>. The first three-arm run here asserted the
    /// control judged nothing salient and got <b>255</b>, which is how this was found: the "control" was a
    /// second copy of the treatment.
    /// <para>The control used a private no-op policy until <see cref="NeutralSaliencePolicy"/> was shipped
    /// for exactly this purpose — a trap that cost a measurement its control is a trap a consumer will hit
    /// too, so the fix belongs in the library rather than in this file.</para></summary>
    private static GraphMemoryEngine NewEngine(InMemoryMemoryGraphStore store, ArmKind arm) =>
        new("e", store,
            agePolicies: [new PerWriteAgePolicy()],
            embedder: arm == ArmKind.Off ? null : new FakeEmbedder(),
            vectors: arm == ArmKind.Off ? null : new InMemoryVectorStore(),
            saliencePolicies: arm == ArmKind.Salience
                ? [new StructuralSaliencePolicy()]
                : [new NeutralSaliencePolicy()]);

    /// <summary>One arm's outcome.</summary>
    /// <param name="Pollution">Share of everything recalls returned that was noise. Measured over the
    /// RETURNED set rather than over the store: an entry that survives and never surfaces has cost the
    /// caller nothing, and design 5.7.0 orders objectives about what a recall hands back.</param>
    /// <param name="Miss">Share of ground-truth relevant entries a recall failed to return — the channel
    /// through which preserved junk actually hurts, by crowding real material out of retention and
    /// admission.</param>
    /// <param name="SalientWrites">How many writes were actually judged salient. <b>Without this the whole
    /// comparison can pass as a silent duplicate of its control</b>, which is the exact failure mode the
    /// sweep's own control reflects over engine state to rule out.</param>
    private readonly record struct Arm(double Pollution, double Miss, int SalientWrites);

    private static async Task<Arm> RunAsync(CorpusShape shape, ArmKind arm)
    {
        var corpus = MemoryCorpus.Generate(shape, Seed);
        var store = new InMemoryMemoryGraphStore();
        var engine = NewEngine(store, arm);
        var first = corpus.Steps.OfType<CorpusWrite>().First().Write;

        var byRef = new Dictionary<string, string>(StringComparer.Ordinal);
        var salientWrites = 0;
        long returned = 0, noise = 0, wanted = 0, missed = 0;

        foreach (var step in corpus.Steps)
            switch (step)
            {
                case CorpusWrite w:
                    var memRef = await engine.RememberAsync(w.Write);
                    byRef[memRef.Id] = CorpusIdOf(w.Write.Content);
                    var node = await store.GetAsync("e", long.Parse(memRef.Id, CultureInfo.InvariantCulture));
                    if (node?.Signals.Get(MemorySignals.WellKnown.Salience) > 1) salientWrites++;
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

        return new Arm(
            returned == 0 ? 0 : (double)noise / returned,
            wanted == 0 ? 0 : (double)missed / wanted,
            salientWrites);
    }

    /// <summary>The corpus id embedded in an entry's content. Every template places it as the second
    /// whitespace-delimited token; diverse noise places it first, so both are covered by taking the first
    /// token that looks like an id rather than by a fixed position.</summary>
    private static string CorpusIdOf(string content)
    {
        foreach (var token in content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            if (token.StartsWith("noise", StringComparison.Ordinal)
                || token.StartsWith("crit", StringComparison.Ordinal)
                || token.StartsWith("topic", StringComparison.Ordinal)
                || token.StartsWith("hot", StringComparison.Ordinal)
                || token.StartsWith("pad", StringComparison.Ordinal)
                || token.StartsWith("attr", StringComparison.Ordinal)
                || token.StartsWith("authoritative", StringComparison.Ordinal))
                return token;
        return content;
    }

    /// <summary><b>THE MEASUREMENT.</b> Three arms on both noise kinds, with the isolated salience effect
    /// reported as <c>Embedder to Salience</c> — never as <c>Off to Salience</c>, which confounds the policy
    /// with the vector search it needs in order to run at all.</summary>
    [Fact]
    public async Task Salience_does_not_preferentially_preserve_textually_diverse_junk()
    {
        var templated = CorpusShape.Default;
        var diverse = CorpusShape.Default with { NoiseKind = CorpusNoiseKind.Diverse };

        var tOff = await RunAsync(templated, ArmKind.Off);
        var tEmb = await RunAsync(templated, ArmKind.Embedder);
        var tSal = await RunAsync(templated, ArmKind.Salience);
        var dOff = await RunAsync(diverse, ArmKind.Off);
        var dEmb = await RunAsync(diverse, ArmKind.Embedder);
        var dSal = await RunAsync(diverse, ArmKind.Salience);

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
                            miss  off / emb / sal            pollution off / emb / sal        salient
               templated    {tOff.Miss:F4} / {tEmb.Miss:F4} / {tSal.Miss:F4}       {tOff.Pollution:F4} / {tEmb.Pollution:F4} / {tSal.Pollution:F4}     {tSal.SalientWrites}
               diverse      {dOff.Miss:F4} / {dEmb.Miss:F4} / {dSal.Miss:F4}       {dOff.Pollution:F4} / {dEmb.Pollution:F4} / {dSal.Pollution:F4}     {dSal.SalientWrites}

               isolated salience effect (embedder -> salience):
                 templated  miss {tSal.Miss - tEmb.Miss:+0.0000;-0.0000}   pollution {tSal.Pollution - tEmb.Pollution:+0.0000;-0.0000}
                 diverse    miss {dSal.Miss - dEmb.Miss:+0.0000;-0.0000}   pollution {dSal.Pollution - dEmb.Pollution:+0.0000;-0.0000}
             """);

        // The arm has to be provably not a duplicate of its control before any delta from it means anything.
        Assert.True(dSal.SalientWrites > 0,
            $"the salience arm judged NOTHING salient, so this comparison measures nothing:\n{table}");
        Assert.Equal(0, dEmb.SalientWrites);

        Assert.True(dSal.Miss - dEmb.Miss <= InversionTolerance,
            $"""
             Part 53's inversion concern REPRODUCED on the channel it can actually act through: preserving
             textually diverse junk cost {dSal.Miss - dEmb.Miss:F4} of additional MISSES against the
             embedder-only control, above the {InversionTolerance:F4} tolerance — real material crowded out
             by junk held for being novel.

             {table}
             """);

        Assert.True(dSal.Pollution - dEmb.Pollution <= InversionTolerance,
            $"salience raised the junk share of recalls past tolerance:\n{table}");

        // On TEMPLATED noise — the junk that can actually reach a recall — salience does the opposite of
        // what the concern predicted: it LOWERS the junk share. Pinned as a floor rather than a point value
        // so it records the direction without freezing a number this instrument cannot defend to 4 places.
        Assert.True(tSal.Pollution - tEmb.Pollution <= InversionTolerance,
            $"salience raised the junk share on reachable junk, which is the concern's own claim:\n{table}");
    }

    /// <summary><b>The finding this study did not go looking for, and the bigger of the two: enabling the
    /// EMBEDDER costs recall quality badly on this corpus, and salience was being blamed for it.</b>
    ///
    /// <para>Turning the vector path on raises the miss rate from <c>0.5357</c> to <c>0.8357</c> on templated
    /// noise — an order of magnitude more movement than anything salience does — because semantic neighbours
    /// compete for the same bounded slots as lexical hits, and on a corpus whose ground truth is lexical they
    /// displace correct answers. The first version of this file compared no-embedder against
    /// embedder-plus-salience and reported the whole gap as the salience inversion reproducing at
    /// <c>+0.1857</c>. It was the embedder.</para>
    ///
    /// <para><b>Pinned rather than fixed.</b> The number is a property of THIS corpus, whose relevance is
    /// defined lexically — a corpus with semantically-related ground truth would likely reverse it, and this
    /// harness cannot say which is more like a real consumer. What it can do is stop the next session from
    /// attributing this cost to whatever policy happens to be switched on beside it. Recorded in
    /// `TASKS.md` as open, with the instrument named.</para></summary>
    [Fact]
    public async Task The_embedder_not_salience_is_what_moves_recall_quality_on_this_corpus()
    {
        var off = await RunAsync(CorpusShape.Default, ArmKind.Off);
        var embedder = await RunAsync(CorpusShape.Default, ArmKind.Embedder);
        var salience = await RunAsync(CorpusShape.Default, ArmKind.Salience);

        var embedderEffect = embedder.Miss - off.Miss;
        var salienceEffect = salience.Miss - embedder.Miss;

        Assert.True(embedderEffect > 0.1,
            $"expected the embedder to move miss substantially; it moved {embedderEffect:+0.0000;-0.0000}");
        Assert.True(Math.Abs(salienceEffect) < Math.Abs(embedderEffect),
            $"""
             salience ({salienceEffect:+0.0000;-0.0000}) should be the SMALLER effect beside the embedder
             ({embedderEffect:+0.0000;-0.0000}) — if that has stopped being true, the attribution in this
             file's remarks is stale and needs re-measuring rather than re-wording.
             """);
    }

    /// <summary><b>Textually diverse junk cannot pollute a recall AT ALL, and that is the structural reason
    /// the inversion does not reproduce through the channel it was predicted on — the opposite of what the
    /// concern expected.</b>
    ///
    /// <para>The hypothesis reasoned that maximally-novel junk would be preserved AND granted admission
    /// priority, and read the result off as <c>PollutionRate</c>. But pollution requires the junk to be
    /// RETRIEVABLE, and a lexical recall only returns what shares terms with the query. Junk diverse enough
    /// to maximise novelty is, by that same property, junk that matches nothing — so it is preserved into a
    /// corner of the store no query reaches. <b>The two properties the concern depends on are in tension:
    /// the more novel the junk, the less reachable it is.</b></para>
    ///
    /// <para>That is why this file measures MISS as well. Retention is the only channel through which
    /// unreachable junk can still hurt — by occupying budget real material needed — and it is the one the
    /// headline fact asserts on. A future change making this number non-zero means junk became reachable,
    /// which is worth failing over even though it would look like an improvement in isolation.</para></summary>
    [Fact]
    public async Task Diverse_junk_never_reaches_a_recall_at_all_which_is_why_it_cannot_pollute_one()
    {
        var diverse = CorpusShape.Default with { NoiseKind = CorpusNoiseKind.Diverse };

        var off = await RunAsync(diverse, ArmKind.Off);
        var salience = await RunAsync(diverse, ArmKind.Salience);

        Assert.Equal(0, off.Pollution, precision: 10);
        Assert.Equal(0, salience.Pollution, precision: 10);
    }

    /// <summary><b>Registering NOTHING leaves salience ON; registering
    /// <see cref="NeutralSaliencePolicy"/> turns it off.</b> Both halves asserted, because the fact is the
    /// asymmetry — a test showing only that the neutral policy silences salience would not warn anyone about
    /// the trap, which is that the intuitive way (an empty collection) does the opposite.</summary>
    [Fact]
    public async Task An_empty_policy_collection_leaves_salience_ON_and_only_the_neutral_policy_turns_it_off()
    {
        var shape = CorpusShape.Default;

        var empty = await SalientWrites(shape, []);
        var neutral = await SalientWrites(shape, [new NeutralSaliencePolicy()]);

        Assert.True(empty > 0,
            $"an empty collection should take the shipped default, not disable salience; judged {empty}");
        Assert.Equal(0, neutral);

        static async Task<int> SalientWrites(CorpusShape shape, IMemorySaliencePolicy[] policies)
        {
            var corpus = MemoryCorpus.Generate(shape, Seed);
            var store = new InMemoryMemoryGraphStore();
            var engine = new GraphMemoryEngine("e", store,
                agePolicies: [new PerWriteAgePolicy()],
                embedder: new FakeEmbedder(),
                vectors: new InMemoryVectorStore(),
                saliencePolicies: policies);

            var judged = 0;
            foreach (var w in corpus.Steps.OfType<CorpusWrite>())
            {
                var memRef = await engine.RememberAsync(w.Write);
                var node = await store.GetAsync("e", long.Parse(memRef.Id, CultureInfo.InvariantCulture));
                if (node?.Signals.Get(MemorySignals.WellKnown.Salience) > 1) judged++;
            }
            return judged;
        }
    }

    /// <summary><b>The `many-candidates` regression — the one measured cost of a shipped default — is
    /// re-checked here against the 3.0 engine rather than left at its 2026-08-12 value.</b>
    ///
    /// <para>`TASKS.md` Part 65 recorded salience making that shape's combined miss rate WORSE by
    /// <c>+0.0169</c> (significant) while improving every other shape: with 40 competitors, admitting salient
    /// entries displaces relevant ones. It was named the obvious first target for a bounded-admission rule.
    /// Two things have changed since, and both could move it — the ranking policy default (RRF) and the
    /// arrival of a verification seam that reorders before the cut.</para>
    ///
    /// <para><b>This asserts the DIRECTION, not the constant.</b> The original figure came from a 30-seed
    /// paired sweep; a single-seed replay cannot reproduce it to four places and pretending otherwise would
    /// be the false-precision trap the corpus docs already warn about. What it can do is fail if the cost
    /// has grown into something a consumer would notice.</para></summary>
    [Fact]
    public async Task The_many_candidates_cost_of_salience_is_bounded_on_the_current_engine()
    {
        var many = CorpusShape.Default with { CandidateCount = 40 };

        var off = await RunAsync(many, ArmKind.Embedder);
        var on = await RunAsync(many, ArmKind.Salience);

        var table = string.Create(CultureInfo.InvariantCulture,
            $"""
             many-candidates (CandidateCount 40)
                            miss     pollution   salient writes
               salience off {off.Miss:F4}   {off.Pollution:F4}      {off.SalientWrites}
               salience on  {on.Miss:F4}   {on.Pollution:F4}      {on.SalientWrites}
               delta        {on.Miss - off.Miss:+0.0000;-0.0000}  {on.Pollution - off.Pollution:+0.0000;-0.0000}
             """);

        Assert.True(on.SalientWrites > 0, $"the salience arm judged nothing, so this measures nothing:\n{table}");
        Assert.Equal(0, off.SalientWrites);

        // MEASURED 2026-08-13, single seed: miss +0.0808, pollution +0.1532. The Part 65 figure (+0.0169
        // combined) came from a 30-seed PAIRED sweep and is a different statistic — a mean of paired
        // differences against one draw — so these are not the same number measured twice and the larger
        // value here is not evidence of a regression against it. What both agree on is the DIRECTION and
        // the mechanism: with 40 competitors, admitting salient entries displaces relevant ones, and the
        // pollution column shows why (salience is admitting substantially more junk into the same slots).
        //
        // Bounds are regression guards on the shipped default at its measured value, not targets. A
        // dense-candidate deployment that does not want this trade now has a supported lever —
        // NeutralSaliencePolicy — which did not exist when the cost was first recorded.
        Assert.True(on.Miss - off.Miss <= 0.15,
            $"the many-candidates miss cost of salience has grown past its measured +0.0808:\n{table}");
        Assert.True(on.Pollution - off.Pollution <= 0.25,
            $"the many-candidates pollution cost of salience has grown past its measured +0.1532:\n{table}");
    }

    /// <summary>The control for the fact above: TEMPLATED junk does reach recalls, in quantity. Without it
    /// "diverse junk never surfaces" would be indistinguishable from "this harness never surfaces noise",
    /// which would make the zero above meaningless.</summary>
    [Fact]
    public async Task Templated_junk_by_contrast_reaches_recalls_constantly()
    {
        var off = await RunAsync(CorpusShape.Default, ArmKind.Off);

        Assert.True(off.Pollution > 0.2,
            $"templated noise should reach recalls constantly; pollution was {off.Pollution:F4}");
    }

    /// <summary>How much salience may raise the junk share, or the miss rate, before the inversion counts as
    /// reproduced.
    /// <para>Not zero: admission priority legitimately reorders what a full store keeps, so a small drift is
    /// the mechanism working rather than inverting. This is a REGRESSION bound — "no worse than what
    /// shipped", never "this is the optimum".</para></summary>
    private const double InversionTolerance = 0.02;
}
