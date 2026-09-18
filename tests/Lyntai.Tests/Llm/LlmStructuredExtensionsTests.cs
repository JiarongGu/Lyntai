using Lyntai.Inference;
using Lyntai;
using Lyntai.Tests.Fakes;
using Lyntai.Text;

namespace Lyntai.Tests.Llm;

public class LlmStructuredExtensionsTests
{
    private static TextRequest Req => new()
    {
        Messages = [TextMessage.User("give me json")],
        JsonSchema = """{"type":"object"}""",
    };

    private static ITextClient Client(FakeLlmProvider provider)
    {
        var options = new LyntaiOptions();
        options.DefaultCandidates.Add(new ProviderCandidate(provider.Id));
        return new TextClient(new TextRouter([provider], new DeadHostTracker(), options), options);
    }

    [Fact]
    public async Task Json_is_extracted_from_prose_and_fences()
    {
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new TextResponse("Sure! Here you go:\n```json\n{\"ok\": true}\n```\nAnything else?", ProviderVerdict.Ok));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal("{\"ok\": true}", reply.Text); // Text IS the parseable object, prose stripped
    }

    [Fact]
    public async Task One_retry_on_unparseable_then_ok()
    {
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new TextResponse("no json here at all", ProviderVerdict.Ok));
        p.Replies.Enqueue(new TextResponse("""{"second": "try"}""", ProviderVerdict.Ok));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Contains("second", reply.Text);
        Assert.Equal(2, p.Calls.Count);
    }

    [Fact]
    public async Task Unparseable_after_retry_is_failed()
    {
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new TextResponse("still prose", ProviderVerdict.Ok));
        p.Replies.Enqueue(new TextResponse("{broken json", ProviderVerdict.Ok));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(ProviderVerdict.Failed, reply.Verdict);
        Assert.Equal(2, p.Calls.Count); // exactly one retry (design §6)
    }

    [Fact]
    public async Task The_retry_appends_a_corrective_message_so_it_differs_from_the_first_shot()
    {
        // a deterministic provider re-sent the IDENTICAL request just repeats its prose — the retry
        // must feed back the bad reply + a JSON-only instruction so the second attempt can differ
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new TextResponse("just prose, sorry", ProviderVerdict.Ok));
        p.Replies.Enqueue(new TextResponse("""{"ok":1}""", ProviderVerdict.Ok));

        await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(2, p.Calls.Count);
        var retry = p.Calls[1].Messages;
        Assert.True(retry.Count > Req.Messages.Count);                                        // strictly more than shot 1
        Assert.Contains(retry, m => m.Role == "assistant" && m.Content == "just prose, sorry"); // bad reply fed back
        Assert.Contains(retry, m => m.Role == "user" && m.Content.Contains("ONLY a single JSON object")); // corrective
    }

    // ---- repairs CODE can make, so the model is not asked to make them --------------------------------
    // `docs/model-tasks.md` §1 files `repair` as a real second call that is fail-CLOSED where every other
    // seam is fail-open, spends a second usage-budget/rate-limit charge, and never returns a cached hit.
    // Every one of these would have cost one.

    [Theory]
    [InlineData("""{"ok": true,}""", "a trailing comma")]
    [InlineData("""{"a": [1, 2,], "b": 2,}""", "trailing commas nested in an array")]
    [InlineData("{\"ok\": true // yes\n}", "a line comment")]
    [InlineData("{/* note */\"ok\": true}", "a block comment")]
    public async Task A_reply_CODE_can_repair_costs_no_second_call(string text, string why)
    {
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new TextResponse(text, ProviderVerdict.Ok));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Single(p.Calls);                                     // the point: no repair round trip
        Assert.True(JsonExtract.IsValid(reply.Text), $"{why}: Text must be STRICTLY valid");
    }

    [Fact]
    public async Task What_it_hands_back_is_RE_SERIALIZED_so_the_strict_guarantee_still_holds()
    {
        // The contract is "an Ok verdict guarantees JsonDocument.Parse(reply.Text) succeeds". Accepting a
        // trailing comma leniently and handing the RAW text back would keep the model call and break that
        // promise for every consumer — which is worse than the round trip it saves.
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new TextResponse("""Here: {"b": 2, "a": [1,2,],}""", ProviderVerdict.Ok));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.DoesNotContain(",}", reply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(",]", reply.Text, StringComparison.Ordinal);
        using var doc = System.Text.Json.JsonDocument.Parse(reply.Text);   // a STRICT parser, as a consumer has
        Assert.Equal(2, doc.RootElement.GetProperty("b").GetInt32());
    }

    [Fact]
    public async Task A_reply_code_canNOT_repair_still_retries_exactly_once()
    {
        // Truncation is the case leniency must NOT paper over: an unbalanced object is missing content, not
        // punctuation, so asking the model again is the only thing that can produce it.
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new TextResponse("""{"cut": "of""", ProviderVerdict.Ok));
        p.Replies.Enqueue(new TextResponse("""{"whole": true}""", ProviderVerdict.Ok));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal(2, p.Calls.Count);
    }

    [Fact]
    public async Task Non_ok_verdicts_pass_through_without_retry()
    {
        var p = new FakeLlmProvider("p");
        p.Replies.Enqueue(new TextResponse("", ProviderVerdict.Refused, Detail: "policy"));

        var reply = await Client(p).CompleteJsonAsync(Req);

        Assert.Equal(ProviderVerdict.Refused, reply.Verdict);
        Assert.Single(p.Calls);
    }
}
