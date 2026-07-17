using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Llm;

namespace Lyntai.Providers.OpenAiCompatible.Payloads;

/// <summary>Canonical <see cref="LlmRequest"/> → OpenAI chat-completions schema.
/// Tool parameter schemas embed as JSON objects; structured output uses response_format.json_schema.</summary>
public static class OpenAiPayload
{
    public static JsonObject Build(LlmRequest req, string model, bool stream)
    {
        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray([.. req.Messages.Select(m => (JsonNode)new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content,
            })]),
            ["stream"] = stream,
        };
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
