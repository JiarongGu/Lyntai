using Lyntai.Llm;

namespace Lyntai.Agents;

/// <summary>One event in a self-driving agent session's stream (the counterpart to <see cref="ToolStep"/>
/// for a loop the agent drives itself). A sealed hierarchy — consumers switch on the concrete type.</summary>
public abstract record AgentStreamEvent;

/// <summary>The session id has been established (fed back as a resume token later). </summary>
public sealed record SessionStarted(string SessionId) : AgentStreamEvent;

/// <summary>A chunk of assistant answer text.</summary>
public sealed record TextDelta(string Text) : AgentStreamEvent;

/// <summary>A chunk of assistant reasoning/thinking text.</summary>
public sealed record Thinking(string Text) : AgentStreamEvent;

/// <summary>The agent invoked a tool. <paramref name="CallId"/> correlates to the matching <see cref="ToolResult"/>.
/// No file path here — that is a specific tool's argument schema, not universal; parse it from ArgumentsJson.</summary>
public sealed record ToolCall(string Name, string ArgumentsJson, string? CallId = null) : AgentStreamEvent;

/// <summary>A tool returned its observation.</summary>
public sealed record ToolResult(string? CallId, string Content, bool IsError) : AgentStreamEvent;

/// <summary>Per-turn usage tick (RAW token counts — the app prices from its own table).</summary>
public sealed record UsageLive(long Input, long Output, long CacheRead) : AgentStreamEvent;

/// <summary>Per-run final usage: RAW counts + the ACTUAL model id. Deliberately NOT LlmUsage (which lacks
/// CacheCreate/model and is the priced path).</summary>
public sealed record UsageFinal(long Input, long Output, long CacheRead, long CacheCreate, string? Model) : AgentStreamEvent;

/// <summary>The single terminal event. Verdict != Ok means the run errored; a no-output run is still
/// diagnosable via Verdict/Subtype/Diagnostic (never silent). Diagnostic is where a CLI adapter packs its
/// stderr tail.</summary>
public sealed record SessionEnded(
    LlmVerdict Verdict, bool IsError, string? Subtype, string? SessionId,
    string? FinalText, string? Diagnostic) : AgentStreamEvent;

/// <summary>Read-only (plan gate) vs write (execute gate) tool policy for a gated agent session.</summary>
public enum AgentToolPolicy { ReadOnly, Write }
