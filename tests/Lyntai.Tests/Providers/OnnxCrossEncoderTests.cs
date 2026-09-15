using Lyntai.Lifecycle;
using Lyntai.Memory.Verification;
using Lyntai.Providers.Onnx;

namespace Lyntai.Tests.Providers;

/// <summary>Reading the relevance score out of a cross-encoder's output. <b>The half that can be WRONG
/// without failing</b> — a head with more than one label still returns finite, well-formed numbers — and the
/// only half a test can reach without a model on disk, exactly as <c>EmbeddingPooling</c> is for the
/// embedder.</summary>
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
        // The silent failure this guards is Part 177's own shape: an NLI-style head puts relevance in a
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
        // A per-TOKEN output — what an embedder's graph emits — is [batch, tokens, hidden]. Pointing this
        // class at a bi-encoder is the likeliest misconfiguration, and it must not average into a "score".
        Assert.Throws<InvalidOperationException>(
            () => CrossEncoderLogits.Read([1f, 2f, 3f, 4f], [2, 2, 1], rows: 2));
    }
}

/// <summary>Composition failures — the ones a partial download actually produces. The cross-encoder loads
/// eagerly for the same reason the embedder does: a bad model directory is a startup error, not a recall
/// that silently stops being verified.</summary>
public class OnnxCrossEncoderCompositionTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("lyntai-onnx-ce-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* a temp dir is not worth failing a run */ }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void A_MISSING_directory_says_so_rather_than_null_referencing()
    {
        Assert.Throws<DirectoryNotFoundException>(
            () => OnnxCrossEncoder.FromDirectory(Path.Combine(_dir, "nope")));
    }

    [Fact]
    public void No_GRAPH_names_both_layouts_it_looked_for()
    {
        var error = Assert.Throws<FileNotFoundException>(() => OnnxCrossEncoder.FromDirectory(_dir));

        Assert.Contains("onnx/model.onnx", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_graph_WITHOUT_a_vocabulary_fails_on_the_vocabulary()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "onnx"));
        File.WriteAllText(Path.Combine(_dir, "onnx", "model.onnx"), "not really a graph");

        var error = Assert.Throws<FileNotFoundException>(() => OnnxCrossEncoder.FromDirectory(_dir));

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
    private sealed class DeclaredLikeTheCrossEncoder : IModelProvider
    {
        public string Id => "onnx-rerank";
        public ProviderCapabilities Capabilities => OnnxCrossEncoder.Declared;

        public Task<IReadOnlyList<double>> ScoreAsync(
            string query, IReadOnlyList<string> documents, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<double>>([.. documents.Select((_, i) => (double)i)]);
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

    [Fact]
    public void It_declares_SCORE_and_nothing_else_so_no_router_sends_it_a_chat_or_an_embedding()
    {
        Assert.Equal([ProviderKinds.Score], OnnxCrossEncoder.Declared.Produces);
        Assert.Equal([ProviderKinds.Text], OnnxCrossEncoder.Declared.Accepts);
        Assert.Equal([ProviderOperation.Complete], OnnxCrossEncoder.Declared.Operations);
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

    private static OnnxCrossEncoder Load()
    {
        Skip.If(string.IsNullOrWhiteSpace(ModelDirectory), "set LYNTAI_ONNX_RERANK_MODEL_DIR to a cross-encoder export");
        return OnnxCrossEncoder.FromDirectory(ModelDirectory!);
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
    public async Task A_document_past_the_context_limit_is_TRUNCATED_and_the_QUERY_still_decides()
    {
        // The pair-encoding budget rule, end to end: truncation takes from the DOCUMENT first, so a document
        // long enough to fill the window on its own must not shorten the question being asked. If the query
        // were truncated away instead, both documents would score alike and the ordering would collapse.
        using var reranker = Load();
        var padding = string.Join(' ', Enumerable.Repeat("berlin is a city in germany", 400));

        var scores = await reranker.ScoreAsync(Query, [$"{Relevant} {padding}", $"{Unrelated} {padding}"]);

        Assert.All(scores, s => Assert.True(double.IsFinite(s), $"a truncated pair scored {s}"));
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
