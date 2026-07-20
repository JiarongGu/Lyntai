using System.Text.Json;
using System.Text.Json.Nodes;

namespace Lyntai.Text;

/// <summary>Reflection-free (trim/AOT-clean) conversion of tool-call argument values to JSON. An LLM/MEAI/MCP
/// client hands tool-call arguments back as a mix of <see cref="JsonElement"/> (parsed off the wire),
/// <see cref="JsonNode"/>, and occasionally boxed CLR primitives; these helpers preserve each value's JSON
/// type (a <c>3</c> stays a number, not <c>"3"</c>) without <see cref="JsonSerializer"/>'s reflection.
/// Shared by the MCP tool-host (<c>ToolFunction</c>) and the MEAI provider bridge so the two can't drift.</summary>
public static class JsonArgs
{
    /// <summary>One argument value → a JSON node, preserving its JSON type. <c>null</c> → <c>null</c>.</summary>
    public static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode n => n.DeepClone(),
        JsonElement e => JsonNode.Parse(e.GetRawText()),
        // typed JsonValue.Create overloads are reflection-free (stays AOT-clean)
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        decimal m => JsonValue.Create(m),
        string s => JsonValue.Create(s),
        var other => JsonValue.Create(other.ToString()), // last-resort fallback
    };

    /// <summary>An argument dictionary → a JSON object string. Null/empty → <c>"{}"</c>.</summary>
    public static string Serialize(IEnumerable<KeyValuePair<string, object?>>? args)
    {
        if (args is null) return "{}";
        var obj = new JsonObject();
        foreach (var (key, value) in args)
            obj[key] = ToNode(value);
        return obj.ToJsonString();
    }

    /// <summary>A JSON object string → the argument dictionary (values are detached <see cref="JsonNode"/>s,
    /// reflection-free) — the inverse of <see cref="Serialize"/>. Null/blank/non-object/unparseable → null.</summary>
    public static IDictionary<string, object?>? Parse(string? argumentsJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return null;
        try
        {
            return JsonNode.Parse(argumentsJson) is JsonObject obj
                ? obj.ToDictionary(kv => kv.Key, kv => (object?)kv.Value?.DeepClone())
                : null;
        }
        catch (JsonException) { return null; }
    }
}
