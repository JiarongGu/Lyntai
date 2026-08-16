using Lyntai.Llm;
using Lyntai.Memory.Verification;

namespace Lyntai.Tests.Memory;

/// <summary>
/// The model-backed verifier's own facts — parsing, bounds, and the fail-open promise.
///
/// <para><b>Why this file did not exist until 2026-08-17, and why that mattered.</b>
/// <see cref="LlmMemoryVerificationPolicy"/> was constructed by NO offline test: its only coverage was
/// <see cref="LlmVerificationLiveTests"/>, which skips without a real model, so every run on every machine
/// and in CI exercised none of it. The whole surface where a model's output meets code — the JSON parse, the
/// ordinal-to-id mapping, the fail-open catch, the non-Ok verdict path — had never executed in the suite.
/// Found by pointing the coverage question at the four policy seams that had no contract (archive
/// Part 86).</para>
///
/// <para>Its sibling <see cref="LlmMemoryAnnotationPolicyTests"/> is the shape this follows, and the
/// precedent for why it is worth having: the whole-codebase review found a real defect in the ANNOTATOR's
/// equivalent surface (a <c>SuggestGrade</c> that was inert against any real model because the prompt never
/// asked for a grade) — caught by asserting on the REQUEST, which no reply-scripted test could see.</para>
/// </summary>
public class LlmMemoryVerificationPolicyTests
{
    private sealed class ScriptedClient(string text, LlmVerdict verdict = LlmVerdict.Ok) : ILlmClient
    {
        public LlmRequest? Last { get; private set; }

        public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
        {
            Last = req;
            return Task.FromResult(new LlmReply(text, verdict));
        }

        public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThrowingClient : ILlmClient
    {
        public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default) =>
            throw new HttpRequestException("the backend is unreachable");

        public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    /// <summary>Honours the token, which the shared <c>FakeLlmClient</c> deliberately does not — the point of
    /// the cancellation fact is the POLICY's catch ordering, and a client that ignored the token would make
    /// it pass vacuously.</summary>
    private sealed class CancellingClient : ILlmClient
    {
        public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new LlmReply("""{"relevant":[1]}""", LlmVerdict.Ok));
        }

        public IAsyncEnumerable<LlmChunk> StreamAsync(LlmRequest req, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class SingleClientFactory(ILlmClient client) : ILlmClientFactory
    {
        public ILlmClient Get(string name) => client;
        public ILlmClient Get() => client;
        public bool TryGet(string name, out ILlmClient c) { c = client; return true; }
        public IReadOnlyList<string> Names => [];
    }

    private static readonly MemoryVerificationCandidate[] Notes =
    [
        new("n1", "the review will be held in the small meeting room"),
        new("n2", "the review covers last quarter's numbers"),
        new("n3", "the small meeting room was repainted last year"),
    ];

    // Spelled out rather than target-typed on purpose: PolicyContractCoverageTests proves coverage by
    // looking for `new <Implementation>(` in a file that also references the contract, so a `new(...)` here
    // would leave the seam reported as uncovered.
    private static LlmMemoryVerificationPolicy Policy(ILlmClient client) =>
        new LlmMemoryVerificationPolicy(new SingleClientFactory(client));

    private static Task<MemoryVerification> VerifyAsync(ILlmClient client, string query = "where is the review?") =>
        Policy(client).VerifyAsync(new MemoryVerificationRequest(query, Notes));

    // ---- the contract, on a working policy and on a broken one --------------------------------------

    [Fact] public Task Never_null() => MemoryVerificationPolicyContract.It_never_returns_null(
        Policy(new ScriptedClient("""{"relevant":[1]}""")));

    [Fact] public Task Ids_were_shown() => MemoryVerificationPolicyContract.Every_id_it_returns_was_one_it_was_shown(
        Policy(new ScriptedClient("""{"relevant":[1,3]}""")));

    [Fact] public Task No_duplicates() => MemoryVerificationPolicyContract.It_returns_no_duplicates(
        Policy(new ScriptedClient("""{"relevant":[1,1,2]}""")));

    [Fact] public Task Empty_candidates() => MemoryVerificationPolicyContract.An_empty_candidate_set_is_no_opinion(
        Policy(new ScriptedClient("""{"relevant":[1]}""")));

    [Fact] public Task Fails_open() =>
        MemoryVerificationPolicyContract.A_failing_policy_yields_NoOpinion_and_not_NothingRelevant(
            Policy(new ThrowingClient()));

    [Fact] public Task Cancellation_propagates() =>
        MemoryVerificationPolicyContract.Cancellation_propagates_rather_than_becoming_no_opinion(
            Policy(new CancellingClient()));

    // ---- this implementation's own surface: turning a reply into a verdict --------------------------

    [Fact]
    public async Task Ordinals_are_mapped_back_to_the_ids_they_stand_for()
    {
        var verdict = await VerifyAsync(new ScriptedClient("""{"relevant":[3,1]}"""));

        Assert.True(verdict.Judged);
        Assert.Equal(["n3", "n1"], verdict.RelevantIds);   // the model's order is the judgement, not the input's
    }

    /// <summary>Models wrap JSON in prose and fences constantly — the same tolerance the annotator carries,
    /// and for the same reason: refusing those fails the feature for something unrelated to the judgement.</summary>
    [Theory]
    [InlineData("""Sure! ```json {"relevant":[1]} ``` hope that helps""")]
    [InlineData("""{"relevant":[1]}""")]
    [InlineData("""  { "relevant": [ 1 ] }  """)]
    public async Task Json_is_found_inside_whatever_the_model_wrapped_it_in(string reply)
    {
        var verdict = await VerifyAsync(new ScriptedClient(reply));

        Assert.True(verdict.Judged);
        Assert.Equal(["n1"], verdict.RelevantIds);
    }

    /// <summary><b>An empty well-formed array is a REAL verdict.</b> "None of these answered it" is the
    /// observation the review log could never previously contain — it is the whole reason the seam exists —
    /// so it must survive parsing as <c>Judged: true</c> rather than collapsing into no-opinion.</summary>
    [Fact]
    public async Task An_empty_relevant_array_is_a_judgement_that_nothing_answered()
    {
        var verdict = await VerifyAsync(new ScriptedClient("""{"relevant":[]}"""));

        Assert.True(verdict.Judged);
        Assert.Empty(verdict.RelevantIds);
    }

    /// <summary>A model that returns one good index and one hallucinated one has still said something true,
    /// so the junk ordinal is DROPPED rather than discarding the whole reply.</summary>
    [Theory]
    [InlineData("""{"relevant":[1,99]}""")]     // out of range
    [InlineData("""{"relevant":[1,0]}""")]      // 0 is not an ordinal — the list is 1-based
    [InlineData("""{"relevant":[1,-2]}""")]
    [InlineData("""{"relevant":[1,"two"]}""")]  // wrong element type
    public async Task A_junk_ordinal_beside_a_good_one_is_dropped_rather_than_fatal(string reply)
    {
        var verdict = await VerifyAsync(new ScriptedClient(reply));

        Assert.True(verdict.Judged);
        Assert.Equal(["n1"], verdict.RelevantIds);
    }

    /// <summary><b>But a reply where NOTHING parsed is no-opinion, not an empty judgement.</b> The asymmetry
    /// against the fact above is the load-bearing part: unparseable must never be recorded as "nothing was
    /// relevant", or a model that has started answering in prose rewrites the review log on every recall.</summary>
    [Theory]
    [InlineData("""{"relevant":[99,100]}""")]   // every ordinal junk
    [InlineData("""{"relevant":"n1"}""")]       // right key, wrong shape
    [InlineData("""{"answers":[1]}""")]         // wrong key
    [InlineData("notes 1 and 3 are relevant")]  // plausible prose, and not what was asked for
    [InlineData("{ this is not json")]
    [InlineData("")]
    public async Task An_unparseable_reply_is_no_opinion_rather_than_nothing_relevant(string reply)
    {
        var verdict = await VerifyAsync(new ScriptedClient(reply));

        Assert.False(verdict.Judged);
        Assert.Empty(verdict.RelevantIds);
    }

    /// <summary>A non-Ok verdict — a refusal, a rate limit, a budget stop — is not a judgement. Reading a
    /// ranking out of one would treat a governance decision as a correctness signal.</summary>
    [Fact]
    public async Task A_refused_reply_is_no_opinion()
    {
        var verdict = await VerifyAsync(new ScriptedClient("""{"relevant":[1]}""", LlmVerdict.Refused));

        Assert.False(verdict.Judged);
    }

    /// <summary>A blank query cannot be judged against, and reaches here whenever a caller recalls with none.
    /// Asking the model anyway spends a call per recall to be told nothing.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_query_is_no_opinion_without_calling_the_model(string query)
    {
        var client = new ScriptedClient("""{"relevant":[1]}""");

        var verdict = await VerifyAsync(client, query);

        Assert.False(verdict.Judged);
        Assert.Null(client.Last);   // and no call was made — this is the latency path of every recall
    }

    /// <summary><b>The prompt must ASK for what the parser reads</b> — the defect class the annotator's own
    /// suite found the hard way, where every offline test passed while the feature was inert against a real
    /// model. Asserted on the REQUEST, which is the only place it is visible.</summary>
    [Fact]
    public async Task The_prompt_numbers_the_notes_and_asks_for_those_numbers()
    {
        var client = new ScriptedClient("""{"relevant":[1]}""");

        await VerifyAsync(client);

        var system = client.Last!.Messages.First().Content;
        var user = client.Last.Messages.Last().Content;

        Assert.Contains("relevant", system, StringComparison.OrdinalIgnoreCase);   // the exact key parsed
        Assert.Contains("where is the review?", user, StringComparison.Ordinal);
        Assert.Contains("the review will be held in the small meeting room", user, StringComparison.Ordinal);
        // the ENGINE's ids must not leak into the prompt: they carry no meaning for the model, and the
        // parser maps ordinals back itself
        Assert.DoesNotContain("n1", user, StringComparison.Ordinal);
    }

    /// <summary>The instruction names no language and gives no examples in one — the same promise the
    /// annotator carries. "It is a JUDGEMENT, not a tokenizer, so it is language-neutral by construction";
    /// an English-shaped instruction would reintroduce the bias this subsystem spent a release removing.</summary>
    [Fact]
    public async Task The_instruction_is_language_neutral()
    {
        var client = new ScriptedClient("""{"relevant":[1]}""");

        await VerifyAsync(client);

        var system = client.Last!.Messages.First().Content;
        Assert.Contains("any language", system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Judge meaning", system, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Billed to the memory consumer and asking for no intermediate reasoning. Both are priced
    /// decisions rather than defaults: this fires on EVERY recall, so it is the spend an operator caps on its
    /// own, and a thinking model measured ~25 s per judgement against ~1.5 s for one that answers directly
    /// (<c>docs/DECISIONS.md</c> D59).</summary>
    [Fact]
    public async Task Every_call_is_tagged_to_the_memory_consumer_and_suppresses_reasoning()
    {
        var client = new ScriptedClient("""{"relevant":[1]}""");

        await VerifyAsync(client);

        Assert.Equal(LlmConsumers.Memory, client.Last!.Consumer);
        Assert.Equal(LlmReasoning.Suppress, client.Last.Reasoning);
    }
}
