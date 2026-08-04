using System.Text.Json;
using Lyntai.Llm;
using Lyntai.Llm.Cli;

namespace Lyntai.Providers.CodexCli;

/// <summary>Translates one line of <c>codex exec --json</c> output (JSONL) into a provider event.
///
/// The envelope below was MEASURED against codex-cli 0.146.0 (2026-08-04), not inferred — including one
/// successful turn (through the <c>--oss</c> local-model path, so no tokens were spent) and one failed turn:
/// <code>
/// {"type":"thread.started","thread_id":"…"}
/// {"type":"turn.started"}
/// {"type":"item.completed","item":{"id":"item_0","type":"error","message":"Model metadata … not found"}}
/// {"type":"item.completed","item":{"id":"item_1","type":"agent_message","text":"ok"}}
/// {"type":"turn.completed","usage":{"input_tokens":6489,"cached_input_tokens":0,"cache_write_input_tokens":0,
///                                  "output_tokens":2,"reasoning_output_tokens":0}}
/// {"type":"error","message":"Reconnecting... 2/5 (unexpected status 401 Unauthorized …)"}
/// {"type":"turn.failed","error":{"message":"unexpected status 401 Unauthorized …"}}
/// </code>
///
/// Two measurements shape the mapping, and both would be easy to get wrong by guessing:
/// <list type="bullet">
/// <item>a bare <c>error</c> line, and an <c>item.completed</c> whose item type is <c>error</c>, are NOT
///   terminal — both appeared in the run that went on to SUCCEED (a retry notice and a model-metadata
///   warning). Only <c>turn.failed</c> means the turn failed, so only it maps to
///   <see cref="CliOutputEventKind.Failure"/>.</item>
/// <item><c>turn.completed</c> carries usage but NO text, so it is a result event with empty text — the
///   answer arrives in the preceding <c>agent_message</c> item, which the engine keeps.</item>
/// </list>
///
/// Tolerant: unknown/malformed lines (including codex's non-JSON <c>ERROR codex_api::…</c> tracing) become
/// <see cref="CliOutputEvent.Ignored"/>, never a throw.</summary>
internal static class CodexJsonlParser
{
    public static CliOutputEvent Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return CliOutputEvent.Ignored;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeEl) || typeEl.ValueKind != JsonValueKind.String)
                return CliOutputEvent.Ignored;

            return typeEl.GetString() switch
            {
                "item.completed" => ParseItem(root),
                "turn.completed" => CliOutputEvent.Result("", ReadUsage(root)),
                "turn.failed" => CliOutputEvent.Failure(FailureMessage(root)),
                // "error" is a NOTICE, not a verdict: codex logs its reconnect attempts this way and then
                // succeeds. Swallowing it here is deliberate — turn.failed is the authority.
                _ => CliOutputEvent.Ignored,
            };
        }
        catch (JsonException)
        {
            return CliOutputEvent.Ignored; // codex interleaves plain-text tracing lines with the JSONL
        }
    }

    /// <summary>An <c>item.completed</c> is the answer only when the item is an <c>agent_message</c>; every
    /// other item type (an <c>error</c> warning, a command/tool item) is chatter to the provider seam.</summary>
    private static CliOutputEvent ParseItem(JsonElement root)
    {
        if (!root.TryGetProperty("item", out var item) || item.ValueKind != JsonValueKind.Object)
            return CliOutputEvent.Ignored;
        if (!item.TryGetProperty("type", out var kind) || kind.ValueKind != JsonValueKind.String ||
            kind.GetString() != "agent_message")
            return CliOutputEvent.Ignored;

        var text = item.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String
            ? textEl.GetString() ?? ""
            : "";
        return text.Length == 0 ? CliOutputEvent.Ignored : CliOutputEvent.Content(text);
    }

    /// <summary><c>{"usage":{"input_tokens":…,"cached_input_tokens":…,"output_tokens":…}}</c>. Codex reports no
    /// cost, so <see cref="LlmUsage.CostUsd"/> stays null rather than being invented from a token price.</summary>
    private static LlmUsage? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object) return null;
        return new LlmUsage(
            Int(usage, "input_tokens"),
            Int(usage, "output_tokens"),
            Int(usage, "cached_input_tokens"));
    }

    /// <summary><c>turn.failed</c> nests its reason under <c>error.message</c>; fall back to a top-level
    /// <c>message</c>, then to a generic line, so a reshaped envelope still yields a non-empty reason (an empty
    /// failure message would classify as an unhelpful bare failure).</summary>
    private static string FailureMessage(JsonElement root)
    {
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
            error.TryGetProperty("message", out var nested) && nested.ValueKind == JsonValueKind.String &&
            nested.GetString() is { Length: > 0 } message)
            return message;
        if (root.TryGetProperty("message", out var flat) && flat.ValueKind == JsonValueKind.String &&
            flat.GetString() is { Length: > 0 } flatMessage)
            return flatMessage;
        return "codex reported the turn failed";
    }

    private static int Int(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var value)
            ? value
            : 0;
}
