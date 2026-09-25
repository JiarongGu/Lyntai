using Lyntai.Inference;
using Lyntai.Memory;
using Lyntai.Memory.Annotation;
using Lyntai.Memory.Verification;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Memory;

/// <summary>Both model-backed memory policies read their reply through the shared tolerant JSON reader, so a
/// reply a judge or comparer would accept is accepted here too — a brace AFTER the object, a trailing comma, a
/// comment.</summary>
public class LlmMemoryPolicyParsingTests
{
    private sealed class SingleClientFactory(ITextClient client) : ITextClientFactory
    {
        public ITextClient Get(string name) => client;
        public ITextClient Get() => client;
        public bool TryGet(string name, out ITextClient c) { c = client; return true; }
        public IReadOnlyList<string> Names => [];
    }

    private static FakeTextClient Replying(string text)
    {
        var client = new FakeTextClient();
        client.Replies.Enqueue(new TextResponse(text, ProviderVerdict.Ok));
        return client;
    }

    private static readonly MemoryVerificationCandidate[] Notes =
    [
        new("n1", "the review is in the small meeting room"),
        new("n2", "the review covers last quarter"),
        new("n3", "lunch is at noon"),
    ];

    [Theory]
    [InlineData("""{"relevant":[1]} (the others are about {other things})""")]
    [InlineData("""{"relevant":[1,],}""")]
    [InlineData("""{"relevant":[1] /* best first */}""")]
    public async Task The_verifier_reads_what_a_tolerant_reader_accepts(string reply)
    {
        var policy = new LlmMemoryVerificationPolicy(new SingleClientFactory(Replying(reply)));

        var verdict = await policy.VerifyAsync(new MemoryVerificationRequest("where is the review?", Notes));

        Assert.True(verdict.Judged);
        Assert.Equal(["n1"], verdict.RelevantIds);
    }

    [Theory]
    [InlineData("""{"subjects":["alice"]} and later {"subjects":["bob"]}""")]
    [InlineData("""{"subjects":["alice",],}""")]
    public async Task The_annotator_reads_what_a_tolerant_reader_accepts(string reply)
    {
        var policy = new LlmMemoryAnnotationPolicy(new SingleClientFactory(Replying(reply)));

        var annotation = await policy.AnnotateAsync(
            new MemoryAnnotationRequest(new MemoryWrite("t", "s", "my spouse is Alice"), [], []));

        Assert.Equal(["alice"], annotation.Subjects);
    }

    [Fact]
    public async Task Both_policies_ask_as_memory_with_reasoning_suppressed()
    {
        var judge = Replying("""{"relevant":[]}""");
        var annotator = Replying("""{"subjects":[]}""");

        await new LlmMemoryVerificationPolicy(new SingleClientFactory(judge), new LlmVerificationOptions { Model = "j" })
            .VerifyAsync(new MemoryVerificationRequest("q", Notes));
        await new LlmMemoryAnnotationPolicy(new SingleClientFactory(annotator), new LlmAnnotationOptions { Model = "a" })
            .AnnotateAsync(new MemoryAnnotationRequest(new MemoryWrite("t", "s", "x"), [], []));

        foreach (var (call, model) in new[] { (judge.Calls.Single(), "j"), (annotator.Calls.Single(), "a") })
        {
            Assert.Equal(ProviderConsumers.Memory, call.Consumer);
            Assert.Equal(TextReasoning.Suppress, call.Reasoning);
            Assert.Equal(model, call.Model);
            Assert.Equal(["system", "user"], call.Messages.Select(m => m.Role));
        }
    }
}
