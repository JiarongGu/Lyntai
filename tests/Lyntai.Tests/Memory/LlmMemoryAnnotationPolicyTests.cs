using Lyntai.Llm;
using Lyntai.Memory;
using Lyntai.Memory.Annotation;

namespace Lyntai.Tests.Memory;

/// <summary>
/// The model-backed annotator's own facts — parsing, bounds, and the fail-open promise.
/// <para>The engine-side mechanism is proved separately with a deterministic annotator
/// (<see cref="MemorySubjectLinkingTests"/>). This file is only about turning a reply into an annotation,
/// which is where a model's output meets code and therefore where malformed input has to be handled rather
/// than assumed away.</para>
/// </summary>
public class LlmMemoryAnnotationPolicyTests
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

    /// <summary>Honours the token, which the shared <c>FakeLlmClient</c> deliberately does not — the
    /// cancellation fact is about the POLICY's catch ordering (<c>catch (OperationCanceledException) { throw; }</c>
    /// ahead of the fail-open catch), and a client that ignored the token would make it pass vacuously.</summary>
    private sealed class CancellingClient : ILlmClient
    {
        public Task<LlmReply> CompleteAsync(LlmRequest req, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(new LlmReply("""{"subjects":["spouse"]}""", LlmVerdict.Ok));
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

    // Spelled out rather than target-typed on purpose: PolicyContractCoverageTests proves coverage by
    // looking for `new <Implementation>(` in a file that also references the contract, so a `new(...)` here
    // would leave the seam reported as uncovered.
    private static LlmMemoryAnnotationPolicy Policy(ILlmClient client) =>
        new LlmMemoryAnnotationPolicy(new SingleClientFactory(client));

    // ---- the seam's contract, on a working policy and on a broken one -------------------------------

    [Fact] public Task Never_null() => MemoryAnnotationPolicyContract.It_never_returns_null(
        Policy(new ScriptedClient("""{"subjects":["spouse"]}""")));

    [Fact] public Task Tolerates_no_context() =>
        MemoryAnnotationPolicyContract.It_tolerates_a_first_write_with_no_context_at_all(
            Policy(new ScriptedClient("""{"subjects":["spouse"]}""")));

    [Fact] public Task Fails_open() =>
        MemoryAnnotationPolicyContract.A_failing_policy_yields_no_opinion_rather_than_throwing(
            Policy(new ThrowingClient()));

    [Fact] public Task Cancellation_propagates() =>
        MemoryAnnotationPolicyContract.Cancellation_propagates_rather_than_becoming_no_opinion(
            Policy(new CancellingClient()));

    private static Task<MemoryAnnotation> AnnotateAsync(ILlmClient client,
        LlmAnnotationOptions? options = null, IReadOnlyList<string>? recent = null) =>
        new LlmMemoryAnnotationPolicy(new SingleClientFactory(client), options)
            .AnnotateAsync(new MemoryAnnotationRequest(
                new MemoryWrite("t", "s", "my spouse is Alice"), recent ?? []));

    [Fact]
    public async Task Subjects_are_read_from_the_reply()
    {
        var annotation = await AnnotateAsync(new ScriptedClient("""{"subjects":["spouse","Alice"]}"""));

        Assert.Equal(["spouse", "Alice"], annotation.Subjects);
    }

    /// <summary>Models wrap JSON in prose and fences constantly; refusing those would make the feature fail
    /// for a reason that has nothing to do with the judgement being asked for.</summary>
    [Theory]
    [InlineData("""Sure! ```json {"subjects":["spouse"]} ``` hope that helps""")]
    [InlineData("""{"subjects":["spouse"]}""")]
    [InlineData("""  {"subjects": [ "spouse" ] }  """)]
    public async Task Json_is_found_inside_whatever_the_model_wrapped_it_in(string reply)
    {
        var annotation = await AnnotateAsync(new ScriptedClient(reply));

        Assert.Equal(["spouse"], annotation.Subjects);
    }

    /// <summary><b>Anything unparseable is NO OPINION, never a guess.</b> Salvaging a malformed reply — first
    /// line, split on commas — would invent subjects the model never committed to, and a WRONG subject links
    /// two unrelated facts permanently. A missed link costs one recall; a wrong one corrupts the graph, and
    /// that asymmetry is what decides the behaviour.</summary>
    [Theory]
    [InlineData("spouse, Alice")]                 // plausible, and not what was asked for
    [InlineData("""{"subjects":"spouse"}""")]     // right key, wrong shape
    [InlineData("""{"topics":["spouse"]}""")]     // wrong key
    [InlineData("{ this is not json")]
    [InlineData("")]
    public async Task An_unparseable_reply_yields_no_opinion(string reply)
    {
        var annotation = await AnnotateAsync(new ScriptedClient(reply));

        Assert.Empty(annotation.Subjects);
        Assert.Null(annotation.Grade);
    }

    /// <summary>A non-Ok verdict — a refusal, a rate limit, a budget stop — is not an annotation. Reading
    /// text out of one would treat a governance decision as a judgement.</summary>
    [Fact]
    public async Task A_refused_reply_yields_no_opinion()
    {
        var annotation = await AnnotateAsync(
            new ScriptedClient("""{"subjects":["spouse"]}""", LlmVerdict.Refused));

        Assert.Empty(annotation.Subjects);
    }

    /// <summary><b>Fail-open.</b> Memory that stops accepting facts because a model is down is worse than
    /// memory with no model at all — the engine treats this exactly as having no annotator.</summary>
    [Fact]
    public async Task A_throwing_client_yields_no_opinion()
    {
        var annotation = await AnnotateAsync(new ThrowingClient());

        Assert.Empty(annotation.Subjects);
    }

    /// <summary>Bounded: an over-eager list cannot turn one write into an unbounded number of edges.</summary>
    [Fact]
    public async Task Subjects_are_capped()
    {
        var annotation = await AnnotateAsync(
            new ScriptedClient("""{"subjects":["a","b","c","d","e","f"]}"""),
            new LlmAnnotationOptions { MaxSubjects = 2 });

        Assert.Equal(2, annotation.Subjects.Count);
    }

    /// <summary>A grade is only read when explicitly opted into: a suggested grade decides whether a fact is
    /// admitted unconditionally for the rest of its life, which is a far larger promise than "these two facts
    /// are related".</summary>
    [Theory]
    [InlineData(false, null)]
    [InlineData(true, MemoryGrade.Authoritative)]
    public async Task A_grade_is_read_only_when_opted_into(bool suggest, MemoryGrade? expected)
    {
        var annotation = await AnnotateAsync(
            new ScriptedClient("""{"subjects":["spouse"],"grade":"authoritative"}"""),
            new LlmAnnotationOptions { SuggestGrade = suggest });

        Assert.Equal(expected, annotation.Grade);
    }

    /// <summary><b>The prompt must ASK for every field the parser reads.</b> The fact above scripts a reply
    /// containing a grade, so it exercises the parser and is structurally unable to notice that nothing ever
    /// requested one — and nothing did: the system prompt was a <c>const</c>, used unconditionally, that
    /// mentioned only <c>subjects</c>. So <c>SuggestGrade = true</c> was inert against any real model, while
    /// every offline test passed. Found 2026-08-14 by the whole-codebase review.
    /// <para>This is the shape of defect that only a LIVE model reveals and no fake can — unless the
    /// assertion is made on the REQUEST rather than on the reply, which is what this fact does.</para>
    /// </summary>
    [Fact]
    public async Task The_prompt_asks_for_a_grade_exactly_when_the_option_is_on()
    {
        var on = new ScriptedClient("""{"subjects":["spouse"]}""");
        await AnnotateAsync(on, new LlmAnnotationOptions { SuggestGrade = true });
        var withGrade = on.Last!.Messages[0].Content;
        Assert.Contains("grade", withGrade, StringComparison.OrdinalIgnoreCase);
        // and the exact token the parser compares against, so the model is told what value to send
        Assert.Contains("authoritative", withGrade, StringComparison.OrdinalIgnoreCase);

        var off = new ScriptedClient("""{"subjects":["spouse"]}""");
        await AnnotateAsync(off, new LlmAnnotationOptions { SuggestGrade = false });
        // off by default keeps the instruction lean — a prompt that asks for a field nobody reads is waste,
        // and invites the model to volunteer a grade this policy would then discard
        Assert.DoesNotContain("grade", off.Last!.Messages[0].Content, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Recent facts reach the prompt, and the fact being labelled comes last — the order a reader
    /// needs to resolve a pronoun, and the whole reason context is passed at all.</summary>
    [Fact]
    public async Task Recent_facts_reach_the_prompt_before_the_one_being_labelled()
    {
        var client = new ScriptedClient("""{"subjects":["spouse"]}""");

        await AnnotateAsync(client, recent: ["she works at a hospital"]);

        var user = client.Last!.Messages.Last().Content;
        Assert.Contains("she works at a hospital", user, StringComparison.Ordinal);
        Assert.True(user.IndexOf("she works", StringComparison.Ordinal)
            < user.IndexOf("my spouse is Alice", StringComparison.Ordinal));
    }

    /// <summary><b>The prompt names no language and gives no examples in one.</b> An English-shaped
    /// instruction would quietly reintroduce the bias this subsystem spent a release removing: a Chinese fact
    /// would get an English subject, a later Chinese fact might get a Chinese one, and the two would not
    /// link. Asserted on the instruction itself because it is library surface, not a caller's concern.</summary>
    [Fact]
    public async Task The_instruction_asks_for_subjects_in_the_facts_own_language()
    {
        var client = new ScriptedClient("""{"subjects":["配偶"]}""");

        await AnnotateAsync(client);

        var system = client.Last!.Messages.First().Content;
        Assert.Contains("SAME LANGUAGE", system, StringComparison.Ordinal);
        Assert.Contains("Never translate", system, StringComparison.Ordinal);
    }
}
