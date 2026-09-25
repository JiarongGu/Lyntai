using System.Text.Json;
using Lyntai.Inference;
using Lyntai.Providers.Basic;
using Lyntai.Text;

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
/// Every member is MEASURED, and says against which codex-cli release (<c>docs/DECISIONS.md</c> D35).</summary>
internal static class CodexEnvelope
{
    // ── envelope types ───────────────────────────────────────────────────────

    /// <summary>MEASURED (0.146.0): <c>{"type":"thread.started","thread_id":"…"}</c> — the session id.</summary>
    public const string ThreadStarted = "thread.started";

    /// <summary>MEASURED (0.146.0): carries usage and ends a SUCCESSFUL turn (no text of its own).</summary>
    public const string TurnCompleted = "turn.completed";

    /// <summary>MEASURED (0.146.0): the ONLY terminal failure. The process still exits 0.</summary>
    public const string TurnFailed = "turn.failed";

    /// <summary>MEASURED (0.146.0): one thread item finished, carrying the item's payload.</summary>
    public const string ItemCompleted = "item.completed";

    /// <summary>MEASURED (0.155.1): a tool item BEGINS — emitted for every tool item, so a tool step is
    /// reported the moment it starts; the completion path still stands on its own if one never
    /// arrives.</summary>
    public const string ItemStarted = "item.started";

    // ── item types ───────────────────────────────────────────────────────────

    /// <summary>MEASURED (0.146.0): <c>{"id":…,"type":"agent_message","text":"…"}</c> — the assistant's
    /// answer.</summary>
    public const string AgentMessageItem = "agent_message";

    /// <summary>MEASURED (0.146.0): a NON-terminal warning item ("Model metadata … not found"), seen in a turn
    /// that succeeded. Never a failure.</summary>
    public const string ErrorItem = "error";

    /// <summary>MEASURED (0.155.1): the model's reasoning summary, in the item's <c>text</c>.</summary>
    public const string ReasoningItem = "reasoning";

    // ── field reads ──────────────────────────────────────────────────────────

    /// <summary>The line's <c>type</c> discriminator, or null when the line is not a typed object.</summary>
    public static string? Type(JsonElement root) => WireJson.String(root, "type");

    /// <summary>The <c>item</c> object of an <c>item.started</c>/<c>item.completed</c> line, or null.</summary>
    public static JsonElement? Item(JsonElement root) => WireJson.Object(root, "item");

    /// <summary>MEASURED token counts from a <c>turn.completed</c> line.</summary>
    /// <param name="Input">codex's <c>input_tokens</c>.</param>
    /// <param name="Output">codex's <c>output_tokens</c>.</param>
    /// <param name="CacheRead">codex's <c>cached_input_tokens</c>.</param>
    /// <param name="CacheCreate">codex's <c>cache_write_input_tokens</c>.</param>
    /// <remarks>codex also reports <c>reasoning_output_tokens</c>, which neither
    /// <see cref="Lyntai.Inference.TextUsage"/> nor <see cref="Lyntai.Agents.UsageFinal"/> has a slot for. It is
    /// DROPPED rather than folded into <paramref name="Output"/> — whether <c>output_tokens</c> already
    /// includes it is unmeasured, and adding it would double-count if it does.</remarks>
    public readonly record struct Usage(long Input, long Output, long CacheRead, long CacheCreate);

    /// <summary>Read <c>{"usage":{…}}</c>, or null when the line carries none. codex reports no cost, so
    /// none is ever invented from a token price.</summary>
    public static Usage? ReadUsage(JsonElement root)
    {
        if (WireJson.Object(root, "usage") is not { } usage) return null;

        return new Usage(
            WireJson.Long(usage, "input_tokens"),
            WireJson.Long(usage, "output_tokens"),
            WireJson.Long(usage, "cached_input_tokens"),
            WireJson.Long(usage, "cache_write_input_tokens"));
    }

    /// <summary><c>turn.failed</c> nests its reason under <c>error.message</c>; fall back to a top-level
    /// <c>message</c>, then to a generic line, so a reshaped envelope still yields a non-empty reason (an
    /// empty failure message would classify as an unhelpful bare failure).</summary>
    public static string FailureMessage(JsonElement root)
    {
        if (WireJson.Object(root, "error") is { } error && JsonExtract.StringProperty(error, "message") is { } nested)
            return nested;
        if (JsonExtract.StringProperty(root, "message") is { } flat)
            return flat;
        return "codex reported the turn failed";
    }
}
