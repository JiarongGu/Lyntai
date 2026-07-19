using System.Text.Json;
using Lyntai.Llm;

namespace Lyntai.Providers.ClaudeCli;

public enum StreamJsonEventKind
{
    /// <summary>Assistant text content (a piece of the reply).</summary>
    AssistantText,

    /// <summary>The terminal result line: final text + usage/cost.</summary>
    Result,

    /// <summary>Anything else (system/init, tool chatter, malformed) — ignored by the provider.</summary>
    Other,
}

public sealed record StreamJsonEvent(StreamJsonEventKind Kind, string Text = "", LlmUsage? Usage = null);

/// <summary>Translates one line of `claude --output-format stream-json` output into a provider event.
/// Tolerant: unknown/malformed lines become <see cref="StreamJsonEventKind.Other"/>, never a throw.</summary>
public static class StreamJsonParser
{
    public static StreamJsonEvent Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return new StreamJsonEvent(StreamJsonEventKind.Other);
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                return new StreamJsonEvent(StreamJsonEventKind.Other);

            return typeEl.GetString() switch
            {
                "assistant" => ParseAssistant(root),
                "result" => ParseResult(root),
                _ => new StreamJsonEvent(StreamJsonEventKind.Other),
            };
        }
        catch (JsonException)
        {
            return new StreamJsonEvent(StreamJsonEventKind.Other);
        }
    }

    private static StreamJsonEvent ParseAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg) ||
            !msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return new StreamJsonEvent(StreamJsonEventKind.Other);

        var text = StreamJsonFields.ConcatTextBlocks(content);
        return text.Length == 0
            ? new StreamJsonEvent(StreamJsonEventKind.Other) // tool-use-only blocks etc.
            : new StreamJsonEvent(StreamJsonEventKind.AssistantText, text);
    }

    private static StreamJsonEvent ParseResult(JsonElement root)
    {
        var text = root.TryGetProperty("result", out var r) && r.ValueKind == JsonValueKind.String
            ? r.GetString() ?? ""
            : "";

        LlmUsage? usage = null;
        if (root.TryGetProperty("usage", out var u) && u.ValueKind == JsonValueKind.Object)
        {
            double? cost = root.TryGetProperty("total_cost_usd", out var c) && c.ValueKind == JsonValueKind.Number
                ? c.GetDouble()
                : null;
            usage = new LlmUsage(
                StreamJsonFields.GetLong(u, "input_tokens"),
                StreamJsonFields.GetLong(u, "output_tokens"),
                StreamJsonFields.GetLong(u, "cache_read_input_tokens"),
                cost);
        }
        return new StreamJsonEvent(StreamJsonEventKind.Result, text, usage);
    }
}
