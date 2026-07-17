using Lyntai.Llm;

namespace Lyntai.Tests.Core;

public class LlmTypesTests
{
    [Fact]
    public void Records_construct_with_expected_values()
    {
        var req = new LlmRequest
        {
            Messages = [LlmMessage.System("sys"), LlmMessage.User("hi")],
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
        Assert.Equal(LlmMessage.User("x"), new LlmMessage("user", "x"));
        Assert.Equal(new LlmCandidate("p", "m"), new LlmCandidate("p", "m"));
        Assert.NotEqual(new LlmCandidate("p", "m"), new LlmCandidate("p", null));
        Assert.Equal(new LlmUsage(1, 2, 3, 0.5), new LlmUsage(1, 2, 3, 0.5));
        Assert.Equal(new LlmReply("t", LlmVerdict.Ok), new LlmReply("t", LlmVerdict.Ok));
        // adding tool-call surface must not change equality of tool-call-less replies
        Assert.Equal(new LlmReply("t", LlmVerdict.Ok) { ToolCalls = null }, new LlmReply("t", LlmVerdict.Ok));
        Assert.Equal(new LlmToolCall("id", "t", "{}"), new LlmToolCall("id", "t", "{}"));
    }

    [Fact]
    public void Reply_carries_tool_calls_without_disturbing_the_positional_ctor()
    {
        var reply = new LlmReply("", LlmVerdict.Ok) { ToolCalls = [new LlmToolCall("call_1", "get_weather", """{"city":"Paris"}""")] };
        var call = Assert.Single(reply.ToolCalls!);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("get_weather", call.Name);
        Assert.Equal("""{"city":"Paris"}""", call.ArgumentsJson);
    }

    [Fact]
    public void Tool_message_factories_shape_the_turn()
    {
        var result = LlmMessage.ToolResult("call_1", "18C sunny");
        Assert.Equal("tool", result.Role);
        Assert.Equal("call_1", result.ToolCallId);
        Assert.Equal("18C sunny", result.Content);

        var calls = new[] { new LlmToolCall("call_1", "get_weather", "{}") };
        var assistant = LlmMessage.AssistantToolCalls(calls);
        Assert.Equal("assistant", assistant.Role);
        Assert.Equal("", assistant.Content);                 // never null — payload emits content:null
        Assert.Equal(calls, assistant.ToolCalls);
        Assert.Null(assistant.ToolCallId);
    }

    [Fact]
    public void Chunk_factories_set_kind_and_verdict()
    {
        Assert.Equal(LlmChunkKind.Content, LlmChunk.Content("x").Kind);
        Assert.Equal("x", LlmChunk.Content("x").Text);
        Assert.Equal(LlmChunkKind.Final, LlmChunk.Final().Kind);
        Assert.Equal(LlmVerdict.Ok, LlmChunk.Final().Verdict);
        var err = LlmChunk.Error(LlmVerdict.Timeout, "slow");
        Assert.Equal(LlmChunkKind.Error, err.Kind);
        Assert.Equal(LlmVerdict.Timeout, err.Verdict);
        Assert.Equal("slow", err.Detail);
    }
}
