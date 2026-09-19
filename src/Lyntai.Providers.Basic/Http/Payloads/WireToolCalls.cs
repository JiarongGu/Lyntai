using System.Text.Json;
using Lyntai.Inference;

namespace Lyntai.Providers.Http.Payloads;

/// <summary>Reads a buffered <c>message.tool_calls</c> array. Shared by both chat wires because the
/// difference between them is carried by the VALUES, not the shape: the OpenAI schema sends an id and the
/// arguments as a STRING, Ollama sends no id (one is synthesized) and the arguments as an OBJECT (its JSON
/// text is taken) — and each case is decided per element by its <see cref="JsonValueKind"/>.</summary>
internal static class WireToolCalls
{
    /// <summary>The calls, or null when the message carries none this build can act on.</summary>
    internal static IReadOnlyList<TextToolCall>? Read(JsonElement message)
    {
        if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() == 0)
            return null;

        var result = new List<TextToolCall>();
        var index = 0;
        foreach (var call in calls.EnumerateArray())
        {
            if (call.ValueKind != JsonValueKind.Object ||
                !call.TryGetProperty("function", out var fn) || fn.ValueKind != JsonValueKind.Object ||
                !fn.TryGetProperty("name", out var nameEl) || nameEl.ValueKind != JsonValueKind.String)
            {
                index++;
                continue; // not a function tool call we can act on — skip
            }
            var id = call.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()! : $"call_{index}"; // Ollama gives no id — synthesize a stable one
            var args = "{}";
            if (fn.TryGetProperty("arguments", out var argEl))
                args = argEl.ValueKind switch
                {
                    JsonValueKind.String => argEl.GetString() is { Length: > 0 } s ? s : "{}", // OpenAI: already a JSON string
                    JsonValueKind.Object => argEl.GetRawText(),                                // Ollama: an object → its JSON text
                    _ => "{}",
                };
            result.Add(new TextToolCall(id, nameEl.GetString()!, args));
            index++;
        }
        return result.Count > 0 ? result : null;
    }
}
