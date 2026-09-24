using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Inference;

namespace Lyntai.Providers.Http.Payloads;

/// <summary>Canonical <see cref="TextRequest"/> → OpenAI chat-completions schema.
/// Tool parameter schemas embed as JSON objects; structured output uses response_format.json_schema.</summary>
internal static class OpenAiPayload
{
    /// <summary>Every top-level member <see cref="Build"/> can set — what configured fields may never name.</summary>
    internal static readonly IReadOnlySet<string> WireMembers = new HashSet<string>(StringComparer.Ordinal)
    {
        "model", "messages", "stream", "stream_options", "max_tokens", "temperature", "tools", "response_format",
    };

    /// <summary>Parse <see cref="HttpModelOptions.SuppressReasoningFields"/>: null when unset or blank.</summary>
    /// <exception cref="ArgumentException">The value is not one JSON object, or names a
    /// <see cref="WireMembers"/> member.</exception>
    internal static JsonObject? ParseSuppressReasoningFields(string? json)
    {
        const string name = nameof(HttpModelOptions.SuppressReasoningFields);
        if (string.IsNullOrWhiteSpace(json)) return null;
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException ex)
        {
            throw new ArgumentException($"{name} is not valid JSON: {ex.Message}", name, ex);
        }
        if (node is not JsonObject fields)
        {
            var kind = node?.GetValueKind() switch
            {
                null => "null",
                JsonValueKind.True or JsonValueKind.False => "boolean",
                var k => k.Value.ToString().ToLowerInvariant(),
            };
            throw new ArgumentException(
                $"{name} must be a JSON object whose members are added to the request body; got a JSON {kind}.",
                name);
        }
        foreach (var (key, _) in fields)
            if (WireMembers.Contains(key))
                throw new ArgumentException(
                    $"{name} may not set \"{key}\": the request sets that member itself, and this option only "
                    + "adds members.", name);
        return fields;
    }

    /// <param name="req">The canonical request.</param>
    /// <param name="model">The resolved model id.</param>
    /// <param name="stream">Whether to ask for the SSE stream.</param>
    /// <param name="suppressReasoningFields">Added, as deep copies, when the request asks
    /// <see cref="TextReasoning.Suppress"/>; from <see cref="ParseSuppressReasoningFields"/>.</param>
    public static JsonObject Build(TextRequest req, string model, bool stream,
        JsonObject? suppressReasoningFields = null)
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

        // Last, and only when asked, so every other call's body is byte-identical to one without the option.
        // A copy per request: a JsonNode has exactly one parent, so the configured node cannot be attached itself.
        if (req.Reasoning == TextReasoning.Suppress && suppressReasoningFields is not null)
            foreach (var (key, value) in suppressReasoningFields)
                payload[key] = value?.DeepClone();
        return payload;
    }

    /// <summary>One canonical message → OpenAI schema. A plain turn is {role, content}; an assistant
    /// tool-call turn is {role:"assistant", content:null, tool_calls:[…]} with arguments as a STRING
    /// (OpenAI's shape); a tool-result turn is {role:"tool", tool_call_id, content}.</summary>
    internal static JsonNode ToMessage(TextMessage m)
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
