using Lyntai.Inference;

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
public sealed record UsageLive(long InputTokens, long OutputTokens, long CacheReadTokens) : AgentStreamEvent;

/// <summary>Per-run final usage: RAW counts + the ACTUAL model id. Deliberately NOT TextUsage (which lacks
/// cache-create/model and is the priced path).</summary>
public sealed record UsageFinal(long InputTokens, long OutputTokens, long CacheReadTokens, long CacheCreateTokens, string? Model) : AgentStreamEvent;

/// <summary>Where one <see cref="PlanEntry"/> stands. A plan entry whose door states a status this enum has no
/// word for reads as null on <see cref="PlanEntry.Status"/>, never as a guess.</summary>
public enum PlanEntryStatus { Pending, InProgress, Completed }

/// <summary>One step of the agent's plan, as the door reports it. <see cref="Status"/> is null when the door
/// states none or a word this library does not know.</summary>
public sealed record PlanEntry(string Content, PlanEntryStatus? Status);

/// <summary>The agent's plan as it now stands. Each event is the WHOLE plan, in the agent's order, and replaces
/// the one before it — never a delta to apply. A door emits one only when its wire carries a plan, so a turn
/// with no plan event means the door sent none, not that the agent had none.</summary>
public sealed record PlanUpdated(IReadOnlyList<PlanEntry> Entries) : AgentStreamEvent;

/// <summary>Whether a <see cref="UsageReading"/> covers the turn that carries it or everything the
/// conversation has used so far.</summary>
public enum UsageScope { Turn, Cumulative }

/// <summary>One usage reading from a door. A count the door did not report is null — UNKNOWN, which is not 0:
/// the older <see cref="UsageLive"/> and <see cref="UsageFinal"/> hold non-nullable counts and so read a count
/// a door never sends as 0.
/// <para>Counts are the door's own, in <see cref="Unit"/>, and are not normalised across doors: whether
/// <see cref="CacheReadTokens"/> sits inside <see cref="InputTokens"/> is the door's convention. Compare a
/// reading only with readings of the same door and <see cref="Scope"/>. Raw counts only — the host prices
/// them, as for <see cref="UsageFinal"/>.</para></summary>
/// <param name="InputTokens">Input count, null = not reported.</param>
/// <param name="OutputTokens">Output count, null = not reported.</param>
/// <param name="CacheReadTokens">Count served from a cache, null = not reported.</param>
/// <param name="CacheCreateTokens">Count written to a cache, null = not reported.</param>
/// <param name="Unit">What the counts count; <see cref="UsageReading.Tokens"/> for every door today.</param>
/// <param name="Scope">The turn this event belongs to, or the conversation so far.</param>
/// <param name="MeasuredAt">When the library read the figure off the door's wire, on its own clock — not a
/// time the door stated.</param>
public sealed record UsageReading(
    long? InputTokens, long? OutputTokens, long? CacheReadTokens, long? CacheCreateTokens,
    string Unit, UsageScope Scope, DateTimeOffset MeasuredAt) : AgentStreamEvent
{
    /// <summary>The <see cref="Unit"/> of a count of model tokens.</summary>
    public const string Tokens = "tokens";
}

/// <summary>The single terminal event, and it ends the TURN: a turn is one <see cref="IAgentSession.StreamAsync"/>
/// call, and a door that adapts a CLI ends it with exactly one of these. The CONVERSATION is not ended by it —
/// <see cref="SessionId"/> is the resume token that carries the conversation into the next turn
/// (<see cref="AgentSessionOptions.ResumeToken"/>). Verdict != Ok means the run errored; a no-output run is
/// still diagnosable via Verdict/Subtype/Diagnostic (never silent). Diagnostic is where a CLI adapter packs its
/// stderr tail.</summary>
public sealed record SessionEnded(
    ProviderVerdict Verdict, bool IsError, string? Subtype, string? SessionId,
    string? FinalText, string? Diagnostic) : AgentStreamEvent;
