using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Llm;

namespace Lyntai.Providers.OpenAiCompatible.Payloads;

/// <summary>Canonical <see cref="LlmRequest"/> → OpenAI chat-completions schema.
/// Tool parameter schemas embed as JSON objects; structured output uses response_format.json_schema.</summary>
internal static class OpenAiPayload
{
    public static JsonObject Build(LlmRequest req, string model, bool stream)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray([.. req.Messages.Select(ToMessage)]),
            ["stream"] = stream,
        };
        // ask the server to append a usage chunk to the SSE stream — without it a streamed call ends with
        // Final(usage: null) and silently bypasses budget/telemetry accounting (buffered calls report fine)
        if (stream) payload["stream_options"] = new JsonObject { ["include_usage"] = true };
        if (req.MaxTokens is not null) payload["max_tokens"] = req.MaxTokens;
        if (req.Temperature is not null) payload["temperature"] = req.Temperature;

        if (req.Tools is { Count: > 0 })
        {
            payload["tools"] = new JsonArray([.. req.Tools.Select(t => (JsonNode)new JsonObject
            {
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = t.Name,
                    ["description"] = t.Description,
                    ["parameters"] = ParseSchema(t.ParametersJsonSchema),
                },
            })]);
        }

        if (req.JsonSchema is not null)
        {
            payload["response_format"] = new JsonObject
            {
                ["type"] = "json_schema",
                ["json_schema"] = new JsonObject
                {
                    ["name"] = "result",
                    ["schema"] = ParseSchema(req.JsonSchema),
                },
            };
        }
        return payload;
    }

    /// <summary>One canonical message → OpenAI schema. A plain turn is {role, content}; an assistant
    /// tool-call turn is {role:"assistant", content:null, tool_calls:[…]} with arguments as a STRING
    /// (OpenAI's shape); a tool-result turn is {role:"tool", tool_call_id, content}.</summary>
    internal static JsonNode ToMessage(LlmMessage m)
    {
        if (m.ToolCalls is { Count: > 0 })
            return new JsonObject
            {
                ["role"] = "assistant",
                // prose the model emitted ALONGSIDE its calls is legal next to tool_calls and must survive
                // the transcript replay (the tool loop preserves it); only a silent turn sends null content
                ["content"] = string.IsNullOrEmpty(m.Content) ? null : m.Content,
                ["tool_calls"] = new JsonArray([.. m.ToolCalls.Select(tc => (JsonNode)new JsonObject
                {
                    ["id"] = tc.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject { ["name"] = tc.Name, ["arguments"] = tc.ArgumentsJson },
                })]),
            };
        if (m.ToolCallId is not null)
            return new JsonObject { ["role"] = "tool", ["tool_call_id"] = m.ToolCallId, ["content"] = m.Content };
        if (m.Role == "user" && m.Attachments is { Count: > 0 }) // OpenAI accepts image parts only on user turns
        {
            // vision: content becomes an array of parts — the text, then one image_url per attachment.
            // Build via the params JsonArray ctor with JsonNode-typed elements (as the tool_calls array
            // above does); the JsonArray.Add<T>(T) overload is flagged trim/AOT-unsafe, this path isn't.
            JsonNode text = new JsonObject { ["type"] = "text", ["text"] = m.Content };
            var images = m.Attachments.Select(a => (JsonNode)new JsonObject
            {
                ["type"] = "image_url",
                ["image_url"] = new JsonObject { ["url"] = a.Url() },
            });
            return new JsonObject { ["role"] = m.Role, ["content"] = new JsonArray([text, .. images]) };
        }
        return new JsonObject { ["role"] = m.Role, ["content"] = m.Content };
    }

    /// <summary>Parse a JSON arguments string into a JSON object node (empty object on failure) — used
    /// where a dialect wants arguments embedded as an object rather than a string (Ollama).</summary>
    internal static JsonNode ParseObject(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new JsonObject();
        try { return JsonNode.Parse(json) ?? new JsonObject(); }
        catch (JsonException) { return new JsonObject(); }
    }

    /// <summary>Schemas arrive as strings on the canonical request but must be embedded as JSON
    /// OBJECTS (a string-encoded schema is the classic interop bug this normalizes away).</summary>
    internal static JsonNode ParseSchema(string? schemaJson)
    {
        if (string.IsNullOrWhiteSpace(schemaJson)) return new JsonObject { ["type"] = "object" };
        try
        {
            return JsonNode.Parse(schemaJson) ?? new JsonObject { ["type"] = "object" };
        }
        catch (JsonException)
        {
            return new JsonObject { ["type"] = "object" };
        }
    }
}
