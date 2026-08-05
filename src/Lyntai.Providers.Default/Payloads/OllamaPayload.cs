using System.Text.Json.Nodes;
using Lyntai.Llm;
using Microsoft.Extensions.Logging;

namespace Lyntai.Providers.OpenAiCompatible.Payloads;

/// <summary>Canonical <see cref="LlmRequest"/> → Ollama /api/chat schema: sampling knobs live under
/// <c>options</c> (num_predict/num_ctx), structured output is a top-level <c>format</c> schema object.</summary>
internal static class OllamaPayload
{
    /// <summary>Build the /api/chat request body for one canonical request.</summary>
    /// <param name="req">The canonical request.</param>
    /// <param name="model">The resolved model id.</param>
    /// <param name="stream">Whether to ask for the NDJSON stream.</param>
    /// <param name="numCtx">Ollama's context window (<c>options.num_ctx</c>), when configured.</param>
    /// <param name="logger">Where an attachment this schema cannot carry is REPORTED — see
    /// <see cref="ToMessage"/>. Null logs nothing.</param>
    public static JsonObject Build(LlmRequest req, string model, bool stream, int? numCtx = null,
        ILogger? logger = null)
    {
        var options = new JsonObject();
        if (req.MaxTokens is not null) options["num_predict"] = req.MaxTokens;
        if (req.Temperature is not null) options["temperature"] = req.Temperature;
        if (numCtx is not null) options["num_ctx"] = numCtx;

        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = new JsonArray([.. req.Messages.Select(m => ToMessage(m, logger))]),
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
    /// content} (Ollama correlates by order, not tool_call_id); and an image travels in a SIBLING
    /// <c>images</c> array of raw base64, not in an OpenAI-style content-parts array.</summary>
    /// <param name="m">The canonical message.</param>
    /// <param name="logger">Where an attachment this schema cannot carry is reported. Null logs nothing.</param>
    internal static JsonNode ToMessage(LlmMessage m, ILogger? logger = null)
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
        if (m.Role == "user" && m.Attachments is { Count: > 0 }) // Ollama takes images only on a user turn
        {
            // vision: /api/chat keeps the text in `content` and puts the images in a SIBLING `images` array
            // of RAW base64 — no data: prefix and no parts array (that shape is OpenAI's). Built via the
            // params JsonArray ctor with JsonNode-typed elements, as OpenAiPayload does and for the same
            // reason: the JsonArray.Add<T>(T) overload is flagged trim/AOT-unsafe.
            JsonNode?[] images =
            [
                .. m.Attachments.Where(a => a.Data is { Length: > 0 })
                    .Select(a => (JsonNode?)Convert.ToBase64String(a.Data!)),
            ];

            // An attachment carrying only a remote Uri has nothing to inline, and /api/chat has no URL form
            // to send it in. REPORT it rather than dropping it in silence — the same thing the CLI paths do
            // for every capability they cannot honour, and the only signal a caller gets that the model was
            // asked about an image it never received.
            var undeliverable = m.Attachments.Count - images.Length;
            if (undeliverable > 0)
                logger?.LogWarning(
                    "ollama /api/chat cannot deliver {Count} attachment(s) on this turn: its images array " +
                    "takes inline base64 only, and these carry no bytes (a remote Uri). Inline them " +
                    "(LlmMessage.UserWithImage) or point the provider at Ollama's OpenAI-compatible /v1 " +
                    "surface via AddOpenAiCompatibleProvider, which accepts an image URL.", undeliverable);

            if (images.Length > 0)
                return new JsonObject
                {
                    ["role"] = m.Role,
                    ["content"] = m.Content,
                    ["images"] = new JsonArray(images),
                };
        }
        return new JsonObject { ["role"] = m.Role, ["content"] = m.Content };
    }
}
