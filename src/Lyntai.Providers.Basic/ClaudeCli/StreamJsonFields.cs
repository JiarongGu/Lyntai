using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Shared field-extraction for the <c>claude --output-format stream-json</c> wire format, so the
/// two readers of it (<see cref="StreamJsonParser"/> — provider events; <see cref="StreamJsonAgentReader"/>
/// — agent-session events) can't drift on how a usage number or a text-block array is read.</summary>
internal static class StreamJsonFields
{
    /// <summary>Every usage field a terminal <c>result</c> line can carry. Each reader PROJECTS what its
    /// event type holds (the provider's <c>TextUsage</c> has cost but no cache-create; the agent session's
    /// <c>UsageFinal</c> has cache-create but deliberately no cost) — the wire-format knowledge lives here
    /// once so the two can't drift on field names.</summary>
    public readonly record struct WireUsage(long Input, long Output, long CacheRead, long CacheCreate, double? CostUsd);

    /// <summary>Read the <c>usage</c> object (+ root <c>total_cost_usd</c>) off a terminal result line;
    /// null when no usage object is present.</summary>
    public static WireUsage? ReadUsage(JsonElement root)
    {
        if (WireJson.Object(root, "usage") is not { } u) return null;
        double? cost = root.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number
            ? c.GetDouble()
            : null;
        return new WireUsage(
            WireJson.Long(u, "input_tokens"),
            WireJson.Long(u, "output_tokens"),
            WireJson.Long(u, "cache_read_input_tokens"),
            WireJson.Long(u, "cache_creation_input_tokens"),
            cost);
    }

    /// <summary>Concatenate the text of every <c>{"type":"text","text":…}</c> block in a content array
    /// (Anthropic message content is an array of typed blocks). Empty for a non-array / no text blocks; a
    /// block that is not an object is skipped.</summary>
    public static string ConcatTextBlocks(JsonElement content)
    {
        if (content.ValueKind != JsonValueKind.Array) return "";
        return string.Concat(content.EnumerateArray()
            .Where(b => WireJson.String(b, "type") == "text")
            .Select(b => WireJson.String(b, "text") ?? ""));
    }
}
