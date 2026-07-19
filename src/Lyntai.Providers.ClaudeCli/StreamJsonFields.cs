using System.Text.Json;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Shared field-extraction for the <c>claude --output-format stream-json</c> wire format, so the
/// two readers of it (<see cref="StreamJsonParser"/> — provider events; <see cref="StreamJsonAgentReader"/>
/// — agent-session events) can't drift on how a usage number or a text-block array is read.</summary>
internal static class StreamJsonFields
{
    /// <summary>A numeric property as a long, or 0 when absent/non-numeric.</summary>
    public static long GetLong(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt64() : 0;

    /// <summary>Concatenate the text of every <c>{"type":"text","text":…}</c> block in a content array
    /// (Anthropic message content is an array of typed blocks). Empty for a non-array / no text blocks.</summary>
    public static string ConcatTextBlocks(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array) return "";
        return string.Concat(content.EnumerateArray()
            .Where(b => b.TryGetProperty("type", out var t) && t.ValueEquals("text"))
            .Select(b => b.TryGetProperty("text", out var txt) && txt.ValueKind == JsonValueKind.String
                ? txt.GetString() ?? ""
                : ""));
    }
}
