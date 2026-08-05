using System.Text.Json;

namespace Lyntai.Providers.CodexCli;

/// <summary>The codex JSONL envelope's vocabulary and field reads, in ONE place. Both codex readers use it —
/// <see cref="CodexJsonlParser"/> (the text-completion path) and <see cref="CodexAgentReader"/> (the
/// agent-session path) — so the two can never disagree about what a line MEANS.
///
/// <para>That matters most for the one rule a hand-rolled parser gets wrong: <b>only <c>turn.failed</c> is
/// terminal.</b> A bare <c>{"type":"error"}</c> line and an <c>item.completed</c> whose item type is
/// <c>error</c> BOTH appeared in the measured run that went on to succeed (a websocket retry notice and a
/// model-metadata warning), so treating either as terminal fails healthy turns. See
/// <see cref="CodexJsonlParser"/> for the full measured capture.</para>
///
/// Members marked INFERRED have NOT been observed in this repository's capture — they are named so a wrong
/// guess is visible rather than load-bearing.</summary>
internal static class CodexEnvelope
{
    // ── envelope types ───────────────────────────────────────────────────────

    /// <summary>MEASURED: <c>{"type":"thread.started","thread_id":"…"}</c> — the session id.</summary>
    public const string ThreadStarted = "thread.started";

    /// <summary>MEASURED: carries usage and ends a SUCCESSFUL turn (no text of its own).</summary>
    public const string TurnCompleted = "turn.completed";

    /// <summary>MEASURED: the ONLY terminal failure. The process still exits 0.</summary>
    public const string TurnFailed = "turn.failed";

    /// <summary>MEASURED: one thread item finished, carrying the item's payload.</summary>
    public const string ItemCompleted = "item.completed";

    /// <summary>INFERRED: not seen in this repository's capture (whose turn ran no tools). Handled so a
    /// tool step is reported the moment it BEGINS where codex emits one; the completion path below stands
    /// on its own if it never arrives.</summary>
    public const string ItemStarted = "item.started";

    // ── item types ───────────────────────────────────────────────────────────

    /// <summary>MEASURED: <c>{"id":…,"type":"agent_message","text":"…"}</c> — the assistant's answer.</summary>
    public const string AgentMessageItem = "agent_message";

    /// <summary>MEASURED: a NON-terminal warning item ("Model metadata … not found"), seen in a turn that
    /// succeeded. Never a failure.</summary>
    public const string ErrorItem = "error";

    /// <summary>INFERRED: the item type expected to carry the model's reasoning summary.</summary>
    public const string ReasoningItem = "reasoning";

    // ── field reads ──────────────────────────────────────────────────────────

    /// <summary>The line's <c>type</c> discriminator, or null when the line is not a typed object.</summary>
    public static string? Type(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("type", out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>A string-valued property, or null when absent or not a string.</summary>
    public static string? StringField(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object &&
        parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
            ? el.GetString()
            : null;

    /// <summary>The <c>item</c> object of an <c>item.started</c>/<c>item.completed</c> line, or null.</summary>
    public static JsonElement? Item(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        root.TryGetProperty("item", out var item) && item.ValueKind == JsonValueKind.Object
            ? item
            : null;

    /// <summary>MEASURED token counts from a <c>turn.completed</c> line.</summary>
    /// <param name="Input">codex's <c>input_tokens</c>.</param>
    /// <param name="Output">codex's <c>output_tokens</c>.</param>
    /// <param name="CacheRead">codex's <c>cached_input_tokens</c>.</param>
    /// <param name="CacheCreate">codex's <c>cache_write_input_tokens</c>.</param>
    /// <remarks>codex also reports <c>reasoning_output_tokens</c>, which neither
    /// <see cref="Lyntai.Llm.LlmUsage"/> nor <see cref="Lyntai.Agents.UsageFinal"/> has a slot for. It is
    /// DROPPED rather than folded into <paramref name="Output"/> — whether <c>output_tokens</c> already
    /// includes it is unmeasured, and adding it would double-count if it does.</remarks>
    public readonly record struct Usage(long Input, long Output, long CacheRead, long CacheCreate);

    /// <summary>Read <c>{"usage":{…}}</c>, or null when the line carries none. codex reports no cost, so
    /// none is ever invented from a token price.</summary>
    public static Usage? ReadUsage(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            return null;

        return new Usage(
            Long(usage, "input_tokens"),
            Long(usage, "output_tokens"),
            Long(usage, "cached_input_tokens"),
            Long(usage, "cache_write_input_tokens"));
    }

    /// <summary><c>turn.failed</c> nests its reason under <c>error.message</c>; fall back to a top-level
    /// <c>message</c>, then to a generic line, so a reshaped envelope still yields a non-empty reason (an
    /// empty failure message would classify as an unhelpful bare failure).</summary>
    public static string FailureMessage(JsonElement root)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object &&
            StringField(error, "message") is { Length: > 0 } nested)
            return nested;
        if (StringField(root, "message") is { Length: > 0 } flat)
            return flat;
        return "codex reported the turn failed";
    }

    private static long Long(JsonElement parent, string name) =>
        parent.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var value)
            ? value
            : 0;
}
