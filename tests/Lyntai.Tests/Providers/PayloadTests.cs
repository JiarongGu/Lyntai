using System.Text.Json.Nodes;
using Lyntai.Llm;
using Lyntai.Providers.OpenAiCompatible.Payloads;

namespace Lyntai.Tests.Providers;

public class PayloadTests
{
    private static LlmRequest Req => new()
    {
        Messages = [LlmMessage.System("be brief"), LlmMessage.User("hi")],
        MaxTokens = 128,
        Temperature = 0.3,
        Tools = [new LlmTool("lookup", "find things", """{"type":"object","properties":{"q":{"type":"string"}}}""")],
        JsonSchema = """{"type":"object","properties":{"ok":{"type":"boolean"}}}""",
    };

    [Fact]
    public void Openai_payload_maps_messages_and_sampling()
    {
        var p = OpenAiPayload.Build(Req, "gpt-x", stream: false);

        Assert.Equal("gpt-x", (string)p["model"]!);
        Assert.False((bool)p["stream"]!);
        var messages = p["messages"]!.AsArray();
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", (string)messages[0]!["role"]!);
        Assert.Equal("hi", (string)messages[1]!["content"]!);
        Assert.Equal(128, (int)p["max_tokens"]!);
        Assert.Equal(0.3, (double)p["temperature"]!);
    }

    [Fact]
    public void Openai_tool_parameters_embed_as_json_object_not_string()
    {
        var p = OpenAiPayload.Build(Req, "m", stream: false);

        var parameters = p["tools"]!.AsArray()[0]!["function"]!["parameters"];
        Assert.IsType<JsonObject>(parameters); // the classic interop bug: a string-encoded schema
        Assert.Equal("object", (string)parameters!["type"]!);
        Assert.NotNull(parameters["properties"]!["q"]);
    }

    [Fact]
    public void Openai_json_schema_lands_in_response_format()
    {
        var p = OpenAiPayload.Build(Req, "m", stream: false);

        var schema = p["response_format"]!["json_schema"]!["schema"];
        Assert.IsType<JsonObject>(schema);
        Assert.Equal("boolean", (string)schema!["properties"]!["ok"]!["type"]!);
    }

    [Fact]
    public void Ollama_payload_uses_options_and_format()
    {
        var p = OllamaPayload.Build(Req, "llama3", stream: true, numCtx: 8192);

        Assert.Equal("llama3", (string)p["model"]!);
        Assert.True((bool)p["stream"]!);
        Assert.Equal(128, (int)p["options"]!["num_predict"]!);
        Assert.Equal(8192, (int)p["options"]!["num_ctx"]!);
        Assert.IsType<JsonObject>(p["format"]); // structured output: schema OBJECT, not a string
        Assert.Equal("boolean", (string)p["format"]!["properties"]!["ok"]!["type"]!);
    }

    [Fact]
    public void Ollama_tools_normalize_the_same_function_envelope()
    {
        var p = OllamaPayload.Build(Req, "m", stream: false);

        var parameters = p["tools"]!.AsArray()[0]!["function"]!["parameters"];
        Assert.IsType<JsonObject>(parameters);
        Assert.Equal("lookup", (string)p["tools"]!.AsArray()[0]!["function"]!["name"]!);
    }

    [Fact]
    public void Schema_round_trips_through_parse()
    {
        const string schema = """{"type":"object","required":["a"],"properties":{"a":{"type":"number"}}}""";

        var node = OpenAiPayload.ParseSchema(schema);

        Assert.Equal(JsonNode.Parse(schema)!.ToJsonString(), node.ToJsonString());
    }

    [Fact]
    public void Malformed_schema_falls_back_to_open_object()
    {
        var node = OpenAiPayload.ParseSchema("{not json");

        Assert.Equal("object", (string)node["type"]!);
    }
}
