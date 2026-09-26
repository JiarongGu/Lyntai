using Lyntai.Inference;
using Lyntai.Tests.Fakes;
using Lyntai.Memory.Verification;
using Lyntai.Providers.Onnx;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Providers;

/// <summary>Reading the relevance score out of a cross-encoder's output. <b>The half that can be WRONG
/// without failing</b> — a head with more than one label still returns finite, well-formed numbers — and the
/// only half a test can reach without a model on disk, exactly as <c>VectorPooling</c> is for the
/// vector backend.</summary>
public class CrossEncoderLogitsTests
{
    [Fact]
    public void Reads_one_score_per_row_from_a_single_logit_head()
    {
        // The published reference pair for ms-marco-MiniLM-L6-v2, in the [batch, 1] shape it emits.
        var scores = CrossEncoderLogits.Read([8.6071f, -4.3201f], [2, 1], rows: 2);

        Assert.Equal(8.6071, scores[0], 4);
        Assert.Equal(-4.3201, scores[1], 4);
    }

    [Fact]
    public void Accepts_an_export_that_SQUEEZED_the_label_axis_away()
    {
        // [batch] rather than [batch, 1]. Refusing it would reject a perfectly good head over a shape
        // carrying exactly the same numbers.
        Assert.Equal([1.5, -0.5], CrossEncoderLogits.Read([1.5f, -0.5f], [2], rows: 2));
    }

    [Fact]
    public void REFUSES_a_MULTI_LABEL_head_rather_than_taking_column_zero()
    {
        // The silent failure this guards (`docs/task-archive.md` Part 215): an NLI-style head puts relevance in a
        // column that is not the first, so reading column 0 returns well-formed numbers in the WRONG order.
        // There is no score meaning "wrong class of model", so this throws — the rule ScoreAsync states.
        var error = Assert.Throws<InvalidOperationException>(
            () => CrossEncoderLogits.Read([0.1f, 0.9f, 0.8f, 0.2f], [2, 2], rows: 2));

        Assert.Contains("ONE logit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void REFUSES_a_row_count_that_disagrees_with_what_was_SENT()
    {
        // Pairing by position is only sound when the counts agree; a short answer silently scores the wrong
        // documents. ScoringVerificationPolicy checks this too, but a caller that does not must not be able
        // to get a mis-paired list out of the provider itself.
        Assert.Throws<InvalidOperationException>(() => CrossEncoderLogits.Read([1f, 2f], [2, 1], rows: 3));
    }

    [Fact]
    public void REFUSES_an_output_with_a_rank_it_cannot_read()
    {
        // A per-TOKEN output — what a vector backend's graph emits — is [batch, tokens, hidden]. Pointing this
        // class at a bi-encoder is the likeliest misconfiguration, and it must not average into a "score".
        Assert.Throws<InvalidOperationException>(
            () => CrossEncoderLogits.Read([1f, 2f, 3f, 4f], [2, 2, 1], rows: 2));
    }
}

/// <summary>The same shape judgement, asked of what the EXPORT DECLARES rather than of a tensor it
/// returned — which is the only place the refusal is actually heard.
///
/// <para><b>Refusing at read time is refusing where nothing is listening.</b> The seam this backend exists
/// for (<c>ScoringVerificationPolicy</c>, D139) is FAIL-OPEN by contract and catches every exception at
/// debug level — pinned one file over by
/// <c>ScoringVerificationPolicyTests.A_backend_that_THROWS_is_no_opinion_rather_than_a_failed_recall</c>. So
/// a multi-label export reaches a deployment as "every recall silently unverified", indistinguishable from
/// having registered no scoring backend at all. Composition is where a throw still stops something.</para>
///
/// <para>Shapes here carry a <c>-1</c> batch axis because that is how ONNX declares a DYNAMIC dimension;
/// a returned tensor never does, which is the whole difference between this and the tests above.</para></summary>
public class CrossEncoderShapeDeclarationTests
{
    [Fact]
    public void A_single_logit_head_with_a_DYNAMIC_batch_axis_is_what_a_cross_encoder_declares()
    {
        Assert.Null(CrossEncoderLogits.ShapeProblem([-1, 1]));
        Assert.Null(CrossEncoderLogits.ShapeProblem([-1]));      // the squeezed export, declared
    }

    [Fact]
    public void A_MULTI_LABEL_head_is_refused_from_its_DECLARATION_before_a_single_pair_is_scored()
    {
        // The finding this test exists for: an NLI-shaped export loads, scores, and returns well-formed
        // numbers in the wrong order — and the fail-open seam turns the resulting throw into silence.
        var problem = CrossEncoderLogits.ShapeProblem([-1, 3]);

        Assert.NotNull(problem);
        Assert.Contains("3 labels", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void A_DYNAMIC_label_axis_declares_too_little_to_refuse_on_and_is_DEFERRED_not_rejected()
    {
        // An exporter may leave both axes open. Refusing that would reject working models over a shape the
        // graph simply declined to state — the tensor it actually returns is judged instead.
        Assert.Null(CrossEncoderLogits.ShapeProblem([-1, -1]));
    }

    [Fact]
    public void A_per_TOKEN_rank_is_refused_from_the_declaration_too()
    {
        Assert.NotNull(CrossEncoderLogits.ShapeProblem([-1, -1, 384]));
    }

    [Theory]
    [InlineData(new[] { 2, 1 })]
    [InlineData(new[] { 2 })]
    [InlineData(new[] { 2, 2 })]
    [InlineData(new[] { 2, 2, 1 })]
    public void The_declaration_check_and_READ_judge_a_shape_the_SAME_way(int[] dimensions)
    {
        // One spelling of "can this carry one score per pair", asked at two times. Two spellings is how a
        // graph gets refused at composition and accepted at read, or the reverse — and the reverse is the
        // one that ships wrong numbers.
        var declared = CrossEncoderLogits.ShapeProblem(dimensions) is null;
        var read = Record.Exception(
            () => CrossEncoderLogits.Read([0.1f, 0.2f, 0.3f, 0.4f], dimensions, rows: 2)) is null;

        Assert.Equal(declared, read);
    }

    // ---- the COMPOSITION call site, not merely the rule it applies -----------------------------------
    // `ScoreOutput` takes the graph's output names and a shape lookup rather than an InferenceSession,
    // for the reason `VectorPooling` and `ShapeProblem` above are separated at all: a decision welded to
    // a native session is a decision no test can reach, so deleting it leaves the suite green. Pinning the
    // RULE and leaving the CALL unpinned is the defect this repository records against `OnnxRegistrationTests`.

    private static string Resolve(params (string Name, int[] Shape)[] outputs) =>
        CrossEncoderLogits.ScoreOutput(
            [.. outputs.Select(o => o.Name)], n => outputs.First(o => o.Name == n).Shape);

    [Fact]
    public void Takes_the_output_named_logits_where_the_export_gives_one()
    {
        Assert.Equal("logits", Resolve(("last_hidden_state", [-1, -1, 384]), ("logits", [-1, 1])));
    }

    [Fact]
    public void Takes_the_SOLE_output_when_the_export_named_it_something_else()
    {
        Assert.Equal("score", Resolve(("score", [-1, 1])));
    }

    [Fact]
    public void REFUSES_a_graph_with_several_outputs_and_no_logits_among_them()
    {
        var error = Assert.Throws<InvalidOperationException>(
            () => Resolve(("last_hidden_state", [-1, -1, 384]), ("pooler_output", [-1, 384])));

        Assert.Contains(nameof(OnnxProvider), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void REFUSES_a_MULTI_LABEL_export_HERE_where_the_fail_open_seam_cannot_swallow_it()
    {
        // The mutation this must kill: dropping the shape check from composition. Everything else about
        // such an export is healthy — it loads, it scores, and it returns finite numbers in the wrong order.
        var error = Assert.Throws<InvalidOperationException>(() => Resolve(("logits", [-1, 3])));

        Assert.Contains("3 labels", error.Message, StringComparison.Ordinal);
        Assert.Contains("-1, 3", error.Message, StringComparison.Ordinal);   // says what it read
    }

    [Fact]
    public void ACCEPTS_an_export_that_declared_neither_axis_rather_than_refusing_what_it_cannot_judge()
    {
        Assert.Equal("logits", Resolve(("logits", [-1, -1])));
    }
}

/// <summary>Composition failures — the ones a partial download actually produces. The cross-encoder loads
/// eagerly for the same reason the vector backend does: a bad model directory is a startup error, not a recall
/// that silently stops being verified.</summary>
public class OnnxCrossEncoderCompositionTests : IDisposable
{
    private readonly ScratchDir _scratch = new("onnx-ce");

    private string Dir => _scratch.Path;

    public void Dispose() => _scratch.Dispose();

    [Fact]
    public void A_MISSING_directory_says_so_rather_than_null_referencing()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => OnnxProvider.FromDirectory(Path.Combine(Dir, "nope")));
    }

    [Fact]
    public void No_GRAPH_names_both_layouts_it_looked_for()
    {
        var error = Assert.Throws<FileNotFoundException>(() => OnnxProvider.FromDirectory(Dir));

        Assert.Contains("onnx/model.onnx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_graph_WITHOUT_a_vocabulary_fails_on_the_vocabulary()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "onnx"));
        File.WriteAllText(Path.Combine(Dir, "onnx", "model.onnx"), "not really a graph");

        var error = Assert.Throws<FileNotFoundException>(() => OnnxProvider.FromDirectory(Dir));

        Assert.Contains("vocab.txt", error.Message, StringComparison.Ordinal);
    }
}

/// <summary>Reachability — <b>the claim D139 makes, pinned without a model on disk.</b>
///
/// <para>The whole point of building this as an <see cref="IModelProvider"/> rather than a bespoke
/// <see cref="IMemoryVerificationPolicy"/> is that <c>AddMemoryScoringVerification</c> already selects any
/// backend declaring <see cref="ProviderKinds.Score"/>. That is a claim about one capability declaration
/// meeting one predicate, and both are cheap — so it is asserted against the REAL declaration and the REAL
/// policy rather than left to a test that skips wherever the model is absent.</para></summary>
public class OnnxCrossEncoderReachabilityTests
{
    /// <summary>Carries the cross-encoder's own declaration, so the predicate below is exercised against
    /// what the class really says rather than against a copy of it.</summary>
    private sealed class DeclaredLikeTheCrossEncoder : IScoreProvider
    {
        public string Id => "onnx-rerank";
        public ProviderCapabilities Capabilities => ScoreDeclaration;

        public Task<ScoreResponse> CallAsync(ScoreRequest request, CancellationToken ct = default) =>
            Task.FromResult(ScoreResponse.Success(
                [.. request.Documents.Select((_, i) => (double)i)]));
    }

    [Fact]
    public async Task The_memory_scoring_seam_SELECTS_a_backend_declaring_what_this_class_declares()
    {
        var policy = new ScoringVerificationPolicy(
            [new DeclaredLikeTheCrossEncoder()], new ScoringVerificationOptions { EndorseCount = 1 });

        var verdict = await policy.VerifyAsync(new MemoryVerificationRequest("q", [
            new MemoryVerificationCandidate("a", "first"),
            new MemoryVerificationCandidate("b", "second"),
        ]));

        // Judged is the load-bearing half: an unselected backend fails OPEN, so a wrong declaration would
        // leave every recall unverified and report nothing at all.
        Assert.True(verdict.Judged);
        Assert.Equal(["b"], verdict.RelevantIds);
    }

    /// <summary>What a provider running the cross-encoder dialect declares. <c>Produces</c> is taken from the
    /// dialect, which decides it (<c>docs/DECISIONS.md</c> <b>D157</b>); <c>Accepts</c> and <c>Operations</c>
    /// restate <c>OnnxProvider</c>'s constructor, which cannot be reached without a real model.</summary>
    internal static readonly ProviderCapabilities ScoreDeclaration = new()
    {
        Accepts = [ProviderKinds.Text],
        Produces = [new OnnxCrossEncoderHead().Produces],
        Operations = [ProviderOperation.Complete],
    };

    /// <summary>The DIALECT is what decides the kind, so this is the claim worth pinning: the same provider
    /// class produces vectors or scores according to which one it was given, and never both.</summary>
    [Fact]
    public void The_dialect_decides_the_kind_so_no_router_sends_it_a_chat_or_the_wrong_call()
    {
        Assert.Equal(ProviderKinds.Score, new OnnxCrossEncoderHead().Produces);
        Assert.Equal(ProviderKinds.Vector, new OnnxPoolingHead(OnnxPooling.Mean, true).Produces);
    }
}

/// <summary>That both <c>AddOnnx*</c> calls hand the container something it will DISPOSE — asserted against
/// the registration they actually perform, not against a restatement of the rule.
///
/// <para><b>`OnnxRegistrationTests` proves the DI premise and cannot prove this.</b> It shows that MS.DI
/// disposes a factory-registered singleton and not an instance-registered one, against a hand-rolled fake —
/// so rewriting either builder call to <c>AddSingleton(instance)</c>, the "tidy-up" its comment warns about,
/// would leave that suite green. Pinning a RULE while the CALL SITE stays unreachable is the shape this
/// repository records in `pitfalls.md`; the fix it prescribes for two copies of one decision is ONE call
/// site, which is also what makes it reachable here.</para></summary>
public class OnnxOwnershipTests
{
    private sealed class TrackingProvider(string produces) : IModelProvider, IDisposable
    {
        public string Id => "tracked";

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [produces],
            Operations = [ProviderOperation.Complete],
        };

        public bool WasDisposed { get; private set; }
        public void Dispose() => WasDisposed = true;
    }

    [Theory]
    [InlineData(ProviderKinds.Vector)]   // AddOnnxProvider's path
    [InlineData(ProviderKinds.Score)]    // AddOnnxCrossEncoder's path
    public void The_CONTAINER_owns_what_either_builder_call_registers(string produces)
    {
        // Both hold a native InferenceSession, so "the container will clean it up" has to be true rather
        // than assumed — and for the instance overload it is not.
        var provider = new TrackingProvider(produces);
        var services = new ServiceCollection();

        services.AddLyntai(b => OnnxBuilderExtensions.RegisterOwned(b, provider));
        using (var built = services.BuildServiceProvider())
            Assert.Contains(provider, built.GetServices<IModelProvider>());

        Assert.True(provider.WasDisposed, "the container disposed nothing — it was handed an instance");
    }

    /// <summary>The registration READS the provider's own capabilities rather than being told which kind it
    /// is (<b>D152</b>), so this pins that the reading is right.</summary>
    [Fact]
    public void Registering_the_CROSS_ENCODER_does_not_claim_the_deployment_can_embed()
    {
        // It produces scores. Claiming otherwise would let AddSemanticMemory compose over a backend that
        // cannot embed, turning a clean composition failure into a runtime one.
        Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddLyntai(b =>
            {
                OnnxBuilderExtensions.RegisterOwned(b, new TrackingProvider(ProviderKinds.Score));
                b.AddSemanticMemory();
            }));

        // …and the mirror: the embedding half of the same package DOES satisfy it, so the assertion above
        // is about the capability rather than about RegisterOwned declaring nothing at all.
        var embeds = new ServiceCollection();
        embeds.AddLyntai(b =>
        {
            OnnxBuilderExtensions.RegisterOwned(b, new TrackingProvider(ProviderKinds.Vector));
            b.AddSemanticMemory();
        });
        Assert.NotNull(embeds.BuildServiceProvider().GetService<Lyntai.Memory.ISemanticMemory>());
    }
}

/// <summary>The cross-encoder against a REAL export — everything above tests a half, because an ONNX graph
/// is protobuf and cannot be hand-built.
///
/// <para>Skipped without <c>LYNTAI_ONNX_RERANK_MODEL_DIR</c>. Point it at a cross-encoder export holding an
/// ONNX graph plus <c>vocab.txt</c> — <c>cross-encoder/ms-marco-MiniLM-L6-v2</c> is what the reference
/// figures below were taken against.</para></summary>
public class OnnxCrossEncoderLiveTests
{
    private const string Query = "How many people live in Berlin?";

    private const string Relevant =
        "Berlin had a population of 3,520,031 registered inhabitants in an area of 891.82 square kilometers.";

    private const string Unrelated = "Berlin is well known for its museums.";

    private static string? ModelDirectory => Environment.GetEnvironmentVariable("LYNTAI_ONNX_RERANK_MODEL_DIR");

    /// <summary>The cross-encoder DIALECT is not optional here, and nothing but a real model would say so:
    /// the default is the bi-encoder one, so omitting it opens a reranker export and reads the wrong
    /// tensor. This suite is env-gated, so that mistake survives a green <c>verify</c>.</summary>
    private static OnnxProvider Load(InputSegmentation? segmentation = null)
    {
        Skip.If(string.IsNullOrWhiteSpace(ModelDirectory), "set LYNTAI_ONNX_RERANK_MODEL_DIR to a cross-encoder export");
        return OnnxProvider.FromDirectory(ModelDirectory!, new OnnxProviderOptions
        {
            Id = "onnx-rerank",
            Produces = ProviderKinds.Score,
            Segmentation = segmentation,
        });
    }

    /// <summary>The same filler past a 512-token window, then the passage that decides.</summary>
    private static string[] FillerThen(params string[] passages)
    {
        var filler = string.Join(' ', Enumerable.Repeat("the weather was mild and the sky stayed grey all week", 60));
        return [.. passages.Select(p => $"{filler} {p}")];
    }

    /// <summary>The whole of D157 in one assertion: one provider class, and what it PRODUCES came from the
    /// dialect it was handed.</summary>
    [SkippableFact]
    public void The_same_provider_class_declares_SCORE_when_given_the_cross_encoder_dialect()
    {
        using var provider = Load();
        Assert.Equal([ProviderKinds.Score], provider.Capabilities.Produces);
    }

    /// <summary>The load-bearing one: agreement with the model's OWN PUBLISHED SCORES, not merely a
    /// plausible ordering.
    ///
    /// <para>Every other assertion here passes on a pipeline that drops <c>token_type_ids</c> — which is
    /// precisely the defect that made the same weights rank this pair BACKWARDS through llama.cpp
    /// (<c>docs/memory-measurements.md</c> §5). Reproducing the card is the only check that can tell a
    /// working segment signal from a missing one.</para></summary>
    [SkippableFact]
    public async Task Reproduces_the_models_own_PUBLISHED_scores_for_the_reference_pair()
    {
        using var reranker = Load();

        var scores = await reranker.ScoreAsync(Query, [Relevant, Unrelated]);

        // Pinned against ms-marco-MiniLM-L6-v2 specifically, so a different export skips rather than
        // failing on numbers that were never about it.
        Skip.IfNot(scores[0] > 5 && scores[1] < 0, "reference figures are ms-marco-MiniLM-L6-v2's");
        Assert.Equal(8.607138, scores[0], 3);
        Assert.Equal(-4.320078, scores[1], 3);
    }

    [SkippableFact]
    public async Task Scores_come_back_in_INPUT_order_rather_than_ranked_order()
    {
        // The contract IModelProvider.ScoreAsync states. A rerank ENDPOINT answers sorted and carries its
        // own indices; an in-process head has no such excuse, and a caller that ranked an already-ranked
        // list would silently promote the wrong candidate.
        using var reranker = Load();

        var forward = await reranker.ScoreAsync(Query, [Relevant, Unrelated]);
        var reversed = await reranker.ScoreAsync(Query, [Unrelated, Relevant]);

        Assert.Equal(forward[0], reversed[1], 4);
        Assert.Equal(forward[1], reversed[0], 4);
    }

    [SkippableFact]
    public async Task By_DEFAULT_a_passage_past_the_context_limit_is_CUT_and_the_documents_tie()
    {
        using var reranker = Load();

        var scores = await reranker.ScoreAsync(Query, FillerThen(Relevant, Unrelated));

        Assert.Equal(scores[0], scores[1]);
    }

    [SkippableFact]
    public async Task With_SEGMENTATION_a_relevant_passage_past_the_context_limit_decides_the_score()
    {
        // D177, end to end: segmented, a document is scored as its BEST window, the query whole in each
        using var reranker = Load(new InputSegmentation());

        var scores = await reranker.ScoreAsync(Query, FillerThen(Relevant, Unrelated));

        Assert.All(scores, s => Assert.True(double.IsFinite(s), $"a segmented pair scored {s}"));
        Assert.True(scores[0] > scores[1], $"relevant {scores[0]:F4} should outrank unrelated {scores[1]:F4}");
    }

    [SkippableFact]
    public async Task Reports_an_id_and_capabilities_like_every_other_backend()
    {
        using var reranker = Load();

        Assert.Equal("onnx-rerank", reranker.Id);
        Assert.True(reranker.IsAvailable);
        Assert.Equal(512, reranker.MaxTokens);
        Assert.Empty(await reranker.ScoreAsync(Query, []));
    }
}
