using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Core;

/// <summary>A candidate id is matched CASE-INSENSITIVELY, in the LLM router as everywhere else.
///
/// <para>Every other id lookup in the tree already worked this way — <c>GenerationRouter</c>,
/// <c>ProviderPoolGuard</c>, <c>IToolRegistry</c>, <c>IJobHandlerRegistry</c>, <c>BoundedProviderPool</c> — and
/// <c>LlmRouter</c> alone did not. The gap was REACHABLE rather than theoretical: <c>ProviderPoolGuard</c>
/// deliberately accepts a pool slot whose case differs from the provider's own <c>Id</c>, so such an instance
/// was validated, built and pooled, and then never selected — the backend was simply never tried, with no error
/// and one debug line.</para>
///
/// <para>The MODEL half of a candidate stays ordinal, which the last test pins: a model id is a vendor's
/// opaque string this library does not own, and two casings of one are not reliably the same
/// endpoint.</para></summary>
public class RouterCandidateIdCaseTests
{
    private static LlmRequest Req => new() { Messages = [LlmMessage.User("hi")] };

    private static LlmRouter Router(params ILlmProvider[] providers) =>
        new(providers, new DeadHostTracker(), new LyntaiOptions());

    [Fact]
    public async Task A_candidate_cased_differently_from_the_providers_own_Id_still_selects_it()
    {
        var provider = new FakeLlmProvider("openai");
        provider.Replies.Enqueue(new LlmReply("served", LlmVerdict.Ok));

        var reply = await Router(provider).CompleteAsync([new LlmCandidate("OpenAI")], Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("served", reply.Text);
        Assert.Single(provider.Calls);
    }

    [Fact]
    public async Task A_case_differing_candidate_does_not_fall_through_to_the_no_live_candidate_reply()
    {
        // the failure mode the fix removes: the ONLY registered backend was skipped as "not registered", and
        // the caller got a synthetic reply naming no provider at all
        var provider = new FakeLlmProvider("Ollama");

        var reply = await Router(provider).CompleteAsync([new LlmCandidate("ollama")], Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.DoesNotContain("no live candidate", reply.Detail ?? "");
    }

    [Fact]
    public async Task The_streaming_door_matches_ids_the_same_way()
    {
        // LiveCandidates is shared, so this is a guard against the two doors drifting apart again
        var provider = new FakeLlmProvider("openai");

        var chunks = new List<LlmChunk>();
        await foreach (var chunk in Router(provider).StreamAsync([new LlmCandidate("OPENAI")], Req))
            chunks.Add(chunk);

        Assert.Equal(1, provider.StreamCalls);
        Assert.Contains(chunks, c => c.Kind == LlmChunkKind.Content && c.Text.Length > 0);
    }

    [Fact]
    public void Ids_differing_only_in_case_dedup_to_ONE_candidate()
    {
        // they resolve to one provider, so leaving both in the list would re-attempt a backend that just
        // failed — and would inflate the count RoutingPolicy.ExemptSoleCandidate reads
        var deduped = CandidateDedup.Dedup([new LlmCandidate("openai"), new LlmCandidate("OpenAI")]);

        Assert.Single(deduped);
        Assert.Equal("openai", deduped[0].ProviderId);   // first wins, spelled as the caller wrote it
    }

    [Fact]
    public void Models_differing_only_in_case_are_NOT_deduped()
    {
        // the deliberate asymmetry: the id is an identity this library owns, the model id is the vendor's
        var deduped = CandidateDedup.Dedup([
            new LlmCandidate("openai", "gpt-x"),
            new LlmCandidate("openai", "GPT-X"),
        ]);

        Assert.Equal(2, deduped.Count);
    }

    [Fact]
    public async Task Only_the_ONLY_capable_backend_being_benched_is_still_exempt_when_it_is_named_twice()
    {
        // the two halves meeting: dedup makes [openai, OpenAI] one candidate, which is what keeps the
        // sole-candidate exemption from being silently withdrawn by a duplicate spelling
        var tracker = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var provider = new FakeLlmProvider("openai");
        provider.Replies.Enqueue(new LlmReply("", LlmVerdict.Failed, Detail: "boom"));
        provider.Replies.Enqueue(new LlmReply("recovered", LlmVerdict.Ok));
        var router = new LlmRouter([provider], tracker, new LyntaiOptions());
        LlmCandidate[] listedTwice = [new("openai"), new("OpenAI")];

        await router.CompleteAsync(listedTwice, Req);
        var second = await router.CompleteAsync(listedTwice, Req);

        Assert.Equal("recovered", second.Text);
        Assert.Equal(2, provider.Calls.Count);
    }
}
