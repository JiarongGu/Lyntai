using Lyntai;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Core;

public class LlmStructuredExtensionsTests
{
    private static LlmRequest Req => new()
    {
        Messages = [LlmMessage.User("give me json")],
        JsonSchema = """{"type":"object"}""",
    };

    private static ILlmClient Client(FakeLlmProvider provider)
    {
        var options = new LyntaiOptions();
        options.DefaultCandidates.Add(new LlmCandidate(provider.Id));
        return new LlmClient(new LlmRouter([provider], new DeadHostTracker(), options), options);
    }

    [Fact]
    public async Task Json_is_extracted_from_prose_and_fences()
    {
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new LlmReply("Sure! Here you go:\n```json\n{\"ok\": true}\n```\nAnything else?", LlmVerdict.Ok));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Equal("{\"ok\": true}", reply.Text); // Text IS the parseable object, prose stripped
    }

    [Fact]
    public async Task One_retry_on_unparseable_then_ok()
    {
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new LlmReply("no json here at all", LlmVerdict.Ok));
        p.Replies.Enqueue(new LlmReply("""{"second": "try"}""", LlmVerdict.Ok));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(LlmVerdict.Ok, reply.Verdict);
        Assert.Contains("second", reply.Text);
        Assert.Equal(2, p.Calls.Count);
    }

    [Fact]
    public async Task Unparseable_after_retry_is_failed()
    {
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new LlmReply("still prose", LlmVerdict.Ok));
        p.Replies.Enqueue(new LlmReply("{broken json", LlmVerdict.Ok));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(LlmVerdict.Failed, reply.Verdict);
        Assert.Equal(2, p.Calls.Count); // exactly one retry (design §6)
    }

    [Fact]
    public async Task The_retry_appends_a_corrective_message_so_it_differs_from_the_first_shot()
    {
        // a deterministic provider re-sent the IDENTICAL request just repeats its prose — the retry
        // must feed back the bad reply + a JSON-only instruction so the second attempt can differ
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new LlmReply("just prose, sorry", LlmVerdict.Ok));
        p.Replies.Enqueue(new LlmReply("""{"ok":1}""", LlmVerdict.Ok));

        await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(2, p.Calls.Count);
        var retry = p.Calls[1].Messages;
        Assert.True(retry.Count > Req.Messages.Count);                                        // strictly more than shot 1
        Assert.Contains(retry, m => m.Role == "assistant" && m.Content == "just prose, sorry"); // bad reply fed back
        Assert.Contains(retry, m => m.Role == "user" && m.Content.Contains("ONLY a single JSON object")); // corrective
    }

    [Fact]
    public async Task Non_ok_verdicts_pass_through_without_retry()
    {
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new LlmReply("", LlmVerdict.Refused, Detail: "policy"));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(LlmVerdict.Refused, reply.Verdict);
        Assert.Single(p.Calls);
    }
}
