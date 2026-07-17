using System.Text.Json.Nodes;
using Lyntai.Llm;

namespace Lyntai.Providers.OpenAiCompatible.Payloads;

/// <summary>Canonical <see cref="LlmRequest"/> → Ollama /api/chat schema: sampling knobs live under
/// <c>options</c> (num_predict/num_ctx), structured output is a top-level <c>format</c> schema object.</summary>
public static class OllamaPayload
{
    public static JsonObject Build(LlmRequest req, string model, bool stream, int? numCtx = null)
    {
        var options = new JsonObject();
        if (req.MaxTokens is not null) options["num_predict"] = req.MaxTokens;
        if (req.Temperature is not null) options["temperature"] = req.Temperature;
        if (numCtx is not null) options["num_ctx"] = numCtx;

        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray([.. req.Messages.Select(ToMessage)]),
            ["stream"] = stream,
        };
        if (options.Count > 0) payload["options"] = options;

        if (req.Tools is { Count: > 0 })
        {
            // same function-tool envelope as OpenAI; parameter schemas are objects here too
            payload["tools"] = new JsonArray([.. req.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = OpenAiPayload.ParseSchema(t.ParametersJsonSchema),
                },
            })]);
        }

        if (req.JsonSchema is not null)
            payload["format"] = OpenAiPayload.ParseSchema(req.JsonSchema); // schema OBJECT, not a string

        return payload;
    }

    /// <summary>One canonical message → Ollama /api/chat schema. Differs from OpenAI: a tool-call turn's
    /// arguments embed as an OBJECT (not a string) and carry no id; a tool result is {role:"tool",
    /// content} (Ollama correlates by order, not tool_call_id).</summary>
    internal static JsonNode ToMessage(LlmMessage m)
    {
        if (m.ToolCalls is { Count: > 0 })
            return new JsonObject
            {
                ["role"] = "assistant",
                ["content"] = m.Content, // "" — Ollama has no null-content requirement
                ["tool_calls"] = new JsonArray([.. m.ToolCalls.Select(tc => (JsonNode)new JsonObject
                {
                    ["function"] = new JsonObject { ["name"] = tc.Name, ["arguments"] = OpenAiPayload.ParseObject(tc.ArgumentsJson) },
                })]),
            };
        if (m.ToolCallId is not null)
            return new JsonObject { ["role"] = "tool", ["content"] = m.Content };
        return new JsonObject { ["role"] = m.Role, ["content"] = m.Content };
    }
}
