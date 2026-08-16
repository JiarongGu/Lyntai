using System.Text.Json;
using Lyntai.Llm;

namespace Lyntai.Providers.ClaudeCli;

internal enum StreamJsonEventKind
{
    /// <summary>Assistant text content (a piece of the reply).</summary>
    AssistantText,

    /// <summary>The terminal result line: final text + usage/cost.</summary>
    Result,

    /// <summary>A terminal result the CLI itself flagged as failed (<c>is_error</c>). Carries the backend's
    /// OWN words so the engine can classify them, which is what turns a 401 into <c>AuthFailed</c> rather
    /// than a bare <c>Failed</c>.
    /// <para>This member did not exist through 2.5.0, so no claude line could reach
    /// <c>CliOutputEventKind.Failure</c> and the engine's in-band-failure precedence was dead code for this
    /// backend — a failed turn came back as an <c>Ok</c> reply carrying whatever text had arrived. The
    /// sibling reader of this same wire format (<c>StreamJsonAgentReader</c>) has always read
    /// <c>is_error</c>; the two halves had drifted.</para></summary>
    Failure,

    /// <summary>Anything else (system/init, tool chatter, malformed) — ignored by the provider.</summary>
    Other,
}

internal sealed record StreamJsonEvent(StreamJsonEventKind Kind, string Text = "", LlmUsage? Usage = null);

/// <summary>Translates one line of `claude --output-format stream-json` output into a provider event.
/// Tolerant: unknown/malformed lines become <see cref="StreamJsonEventKind.Other"/>, never a throw.</summary>
internal static class StreamJsonParser
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

        // shared wire read; LlmUsage projects cost + cache-read (it has no cache-create field)
        var usage = StreamJsonFields.ReadUsage(root) is { } w
            ? new LlmUsage(w.Input, w.Output, w.CacheRead, w.CostUsd)
            : null;

        // The backend's own failure flag OUTRANKS the presence of text: a run that produced partial output
        // and then failed is a failed run, and returning its text as the answer is the defect this reads for.
        // Only an explicit `true` counts — absent or false is the ordinary success path, and a looser test
        // here would fail every healthy turn.
        var isError = root.TryGetProperty("is_error", out var e) && e.ValueKind == JsonValueKind.True;
        if (!isError) return new StreamJsonEvent(StreamJsonEventKind.Result, text, usage);

        // Carry the CLI's OWN words so LlmVerdictClassifier can see them; `subtype` is the machine-readable
        // reason and is included when `result` is empty, so the message is never blank (an empty failure
        // message is what makes the engine fall back to the stderr tail).
        var subtype = root.TryGetProperty("subtype", out var s) && s.ValueKind == JsonValueKind.String
            ? s.GetString()
            : null;
        var message = !string.IsNullOrWhiteSpace(text) ? text
            : !string.IsNullOrWhiteSpace(subtype) ? subtype!
            : "the CLI reported the turn as failed";
        return new StreamJsonEvent(StreamJsonEventKind.Failure, message, usage);
    }
}
