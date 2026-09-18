using Lyntai.Inference;
using Lyntai.Llm;

namespace Lyntai.Tests.Llm;

public class LlmTypesTests
{
    [Fact]
    public void Records_construct_with_expected_values()
    {
        var req = new TextRequest
        {
            Messages = [TextMessage.System("sys"), TextMessage.User("hi")],
            Model = "m1",
            MaxTokens = 100,
            Temperature = 0.2,
        };

        Assert.Equal(2, req.Messages.Count);
        Assert.Equal("system", req.Messages[0].Role);
        Assert.Equal("hi", req.Messages[1].Content);
        Assert.Equal("default", req.Consumer);
        Assert.Null(req.JsonSchema);
        Assert.Null(req.Tools);
    }

    [Fact]
    public void Records_have_value_equality()
    {
        Assert.Equal(TextMessage.User("x"), new TextMessage("user", "x"));
        Assert.Equal(new ProviderCandidate("p", "m"), new ProviderCandidate("p", "m"));
        Assert.NotEqual(new ProviderCandidate("p", "m"), new ProviderCandidate("p", null));
        Assert.Equal(new TextUsage(1, 2, 3, 0.5), new TextUsage(1, 2, 3, 0.5));
        Assert.Equal(new TextResponse("t", ProviderVerdict.Ok), new TextResponse("t", ProviderVerdict.Ok));
        // adding tool-call surface must not change equality of tool-call-less replies
        Assert.Equal(new TextResponse("t", ProviderVerdict.Ok) { ToolCalls = null }, new TextResponse("t", ProviderVerdict.Ok));
        Assert.Equal(new TextToolCall("id", "t", "{}"), new TextToolCall("id", "t", "{}"));
    }

    [Fact]
    public void Reply_carries_tool_calls_without_disturbing_the_positional_ctor()
    {
        var reply = new TextResponse("", ProviderVerdict.Ok) { ToolCalls = [new TextToolCall("call_1", "get_weather", """{"city":"Paris"}""")] };
        var call = Assert.Single(reply.ToolCalls!);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("""{"city":"Paris"}""", call.ArgumentsJson);
    }

    [Fact]
    public void Tool_message_factories_shape_the_turn()
    {
        var result = TextMessage.ToolResult("call_1", "18C sunny");
        Assert.Equal("tool", result.Role);
        Assert.Equal("call_1", result.ToolCallId);
        Assert.Equal("18C sunny", result.Content);

        var calls = new[] { new TextToolCall("call_1", "get_weather", "{}") };
        var assistant = TextMessage.AssistantToolCalls(calls);
        Assert.Equal("assistant", assistant.Role);
        Assert.Equal("", assistant.Content);                 // never null — payload emits content:null
        Assert.Equal(calls, assistant.ToolCalls);
        Assert.Null(assistant.ToolCallId);
    }

    [Fact]
    public void Chunk_factories_set_kind_and_verdict()
    {
        Assert.Equal(TextChunkKind.Content, TextChunk.Content("x").Kind);
        Assert.Equal("x", TextChunk.Content("x").Text);
        Assert.Equal(TextChunkKind.Final, TextChunk.Final().Kind);
        Assert.Equal(ProviderVerdict.Ok, TextChunk.Final().Verdict);
        var err = TextChunk.Error(ProviderVerdict.Timeout, "slow");
        Assert.Equal(TextChunkKind.Error, err.Kind);
        Assert.Equal(ProviderVerdict.Timeout, err.Verdict);
        Assert.Equal("slow", err.Detail);
    }
}
