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
            ["messages"] = new JsonArray([.. req.Messages.Select(m => (JsonNode)new JsonObject
            {
                ["role"] = m.Role,
                ["content"] = m.Content,
            })]),
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
}
