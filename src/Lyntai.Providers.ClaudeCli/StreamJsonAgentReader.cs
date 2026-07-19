using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Llm;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Stateful, per-run translator: feed it each <c>claude --output-format stream-json
/// --include-partial-messages</c> line via <see cref="Read"/>; it yields 0..N <see cref="AgentStreamEvent"/>s.
/// Tolerant — an unknown/malformed line yields nothing, never throws. Remembers the model id across lines
/// so the terminal <see cref="UsageFinal"/> carries it. Line-translation ONLY: it does NOT set
/// <see cref="SessionEnded.Diagnostic"/> (no stderr knowledge — the session runner fills that).</summary>
internal sealed class StreamJsonAgentReader
{
    private string? _model;

    /// <summary>Translates one stream-json line into 0..N events. Never throws.</summary>
    public IEnumerable<AgentStreamEvent> Read(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) yield break;

        JsonDocument? doc = null;
        try
        {
            doc = JsonDocument.Parse(line);
        }
        catch (JsonException)
        {
            yield break;
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("type", out var typeEl) ||
                typeEl.ValueKind != JsonValueKind.String)
                yield break;

            var type = typeEl.GetString();
            switch (type)
            {
                case "system":
                    foreach (var e in ReadSystem(root)) yield return e;
                    break;
                case "stream_event":
                    foreach (var e in ReadStreamEvent(root)) yield return e;
                    break;
                case "assistant":
                    foreach (var e in ReadAssistant(root)) yield return e;
                    break;
                case "user":
                    foreach (var e in ReadUser(root)) yield return e;
                    break;
                case "result":
                    foreach (var e in ReadResult(root)) yield return e;
                    break;
                // Any other type → yield nothing
            }
        }
    }

    // ── system/init ──────────────────────────────────────────────────────────

    private IEnumerable<AgentStreamEvent> ReadSystem(JsonElement root)
    {
        // Capture model early; yield SessionStarted
        if (root.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
            _model = modelEl.GetString();

        if (root.TryGetProperty("session_id", out var sidEl) && sidEl.ValueKind == JsonValueKind.String)
            yield return new SessionStarted(sidEl.GetString()!);
    }

    // ── stream_event (partial content deltas) ────────────────────────────────

    private static IEnumerable<AgentStreamEvent> ReadStreamEvent(JsonElement root)
    {
        if (!root.TryGetProperty("event", out var eventEl) || eventEl.ValueKind != JsonValueKind.Object)
            yield break;

        if (!eventEl.TryGetProperty("type", out var eventTypeEl) || eventTypeEl.ValueKind != JsonValueKind.String)
            yield break;

        // Only content_block_delta carries text/thinking deltas we care about
        if (!eventTypeEl.ValueEquals("content_block_delta"))
            yield break;

        if (!eventEl.TryGetProperty("delta", out var deltaEl) || deltaEl.ValueKind != JsonValueKind.Object)
            yield break;

        if (!deltaEl.TryGetProperty("type", out var deltaTypeEl) || deltaTypeEl.ValueKind != JsonValueKind.String)
            yield break;

        var deltaType = deltaTypeEl.GetString();
        if (deltaType == "text_delta")
        {
            if (deltaEl.TryGetProperty("text", out var textEl) && textEl.ValueKind == JsonValueKind.String)
                yield return new TextDelta(textEl.GetString()!);
        }
        else if (deltaType == "thinking_delta")
        {
            if (deltaEl.TryGetProperty("thinking", out var thinkingEl) && thinkingEl.ValueKind == JsonValueKind.String)
                yield return new Thinking(thinkingEl.GetString()!);
        }
        // Any other delta type → yield nothing
    }

    // ── assistant (complete message, may contain tool_use blocks + usage) ────

    private IEnumerable<AgentStreamEvent> ReadAssistant(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            yield break;

        // Update model if present on the message
        if (msg.TryGetProperty("model", out var modelEl) && modelEl.ValueKind == JsonValueKind.String)
            _model = modelEl.GetString();

        if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var block in content.EnumerateArray())
            {
                if (!block.TryGetProperty("type", out var blockTypeEl) || blockTypeEl.ValueKind != JsonValueKind.String)
                    continue;

                // text blocks: intentionally skipped (already streamed via stream_event deltas)
                if (blockTypeEl.ValueEquals("tool_use"))
                {
                    var name = block.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String
                        ? nameEl.GetString()!
                        : string.Empty;
                    var id = block.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                        ? idEl.GetString()
                        : null;
                    var argsJson = block.TryGetProperty("input", out var inputEl)
                        ? inputEl.GetRawText()
                        : "{}";
                    yield return new ToolCall(name, argsJson, id);
                }
            }
        }

        // Emit UsageLive if usage is present
        if (msg.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            yield return new UsageLive(
                StreamJsonFields.GetLong(usage, "input_tokens"),
                StreamJsonFields.GetLong(usage, "output_tokens"),
                StreamJsonFields.GetLong(usage, "cache_read_input_tokens"));
        }
    }

    // ── user (tool results fed back) ─────────────────────────────────────────

    private static IEnumerable<AgentStreamEvent> ReadUser(JsonElement root)
    {
        if (!root.TryGetProperty("message", out var msg) || msg.ValueKind != JsonValueKind.Object)
            yield break;

        if (!msg.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            yield break;

        foreach (var block in content.EnumerateArray())
        {
            if (!block.TryGetProperty("type", out var blockTypeEl) || !blockTypeEl.ValueEquals("tool_result"))
                continue;

            var callId = block.TryGetProperty("tool_use_id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                ? idEl.GetString()
                : null;

            var isError = block.TryGetProperty("is_error", out var isErrorEl) && isErrorEl.ValueKind == JsonValueKind.True;

            string contentStr = string.Empty;
            if (block.TryGetProperty("content", out var contentEl))
                contentStr = contentEl.ValueKind == JsonValueKind.String
                    ? contentEl.GetString() ?? string.Empty
                    : StreamJsonFields.ConcatTextBlocks(contentEl); // array → concat text blocks; else ""

            yield return new ToolResult(callId, contentStr, isError);
        }
    }

    // ── result (terminal) ────────────────────────────────────────────────────

    private IEnumerable<AgentStreamEvent> ReadResult(JsonElement root)
    {
        var isError = root.TryGetProperty("is_error", out var isErrorEl) && isErrorEl.ValueKind == JsonValueKind.True;

        var subtype = root.TryGetProperty("subtype", out var subtypeEl) && subtypeEl.ValueKind == JsonValueKind.String
            ? subtypeEl.GetString()
            : null;

        var sessionId = root.TryGetProperty("session_id", out var sidEl) && sidEl.ValueKind == JsonValueKind.String
            ? sidEl.GetString()
            : null;

        var finalText = root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == JsonValueKind.String
            ? resultEl.GetString()
            : null;

        // UsageFinal first (if usage present), then SessionEnded
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            yield return new UsageFinal(
                StreamJsonFields.GetLong(usage, "input_tokens"),
                StreamJsonFields.GetLong(usage, "output_tokens"),
                StreamJsonFields.GetLong(usage, "cache_read_input_tokens"),
                StreamJsonFields.GetLong(usage, "cache_creation_input_tokens"),
                _model);
        }

        yield return new SessionEnded(
            Verdict: isError ? LlmVerdict.Failed : LlmVerdict.Ok,
            IsError: isError,
            Subtype: subtype,
            SessionId: sessionId,
            FinalText: finalText,
            Diagnostic: null);
    }
}
