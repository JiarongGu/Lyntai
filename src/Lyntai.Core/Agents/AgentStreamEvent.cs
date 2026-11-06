using Lyntai.Inference;

namespace Lyntai.Agents;

/// <summary>One event in a self-driving agent session's stream (the counterpart to <see cref="ToolStep"/>
/// for a loop the agent drives itself). A sealed hierarchy — consumers switch on the concrete type.</summary>
public abstract record AgentStreamEvent;

/// <summary>The session id has been established (fed back as a resume token later). </summary>
public sealed record SessionStarted(string SessionId) : AgentStreamEvent
{
    /// <summary>Whether the agent announced the session: true for the agent's own start (Claude's <c>init</c>, the
    /// protocol door's open answer, Codex's thread start), false for a later frame that only carries an id the reader had
    /// not seen. A door yields one per id either way.</summary>
    public bool Announced { get; init; } = true;
}

/// <summary>A chunk of assistant answer text.</summary>
public sealed record TextDelta(string Text) : AgentStreamEvent;

/// <summary>A chunk of assistant reasoning/thinking text.</summary>
public sealed record Thinking(string Text) : AgentStreamEvent;

/// <summary>The agent invoked a tool. <paramref name="CallId"/> correlates to the matching <see cref="ToolResult"/>.
/// No file path here — that is a specific tool's argument schema, not universal; parse it from ArgumentsJson.</summary>
public sealed record ToolCall(string Name, string ArgumentsJson, string? CallId = null) : AgentStreamEvent
{
    /// <summary>The <see cref="CallId"/> of the call that spawned the subagent making this one; null for the
    /// session's own call, and for a door that does not say.</summary>
    public string? ParentCallId { get; init; }
}

/// <summary>A tool returned its observation.</summary>
public sealed record ToolResult(string? CallId, string Content, bool IsError) : AgentStreamEvent
{
    /// <summary>The <see cref="ToolCall.CallId"/> of the call that spawned the subagent this result belongs to;
    /// null for the session's own result, and for a door that does not say.</summary>
    public string? ParentCallId { get; init; }
}

/// <summary>The agent started background work: a command it left running, or a subagent it spawned. A turn's
/// <see cref="SessionEnded"/> does not mean the work finished — the agent's binary ends its background work when the
/// turn ends — so a task with no <see cref="AgentTaskEnded"/> by then was still open at the terminal.</summary>
/// <param name="TaskId">The agent's id for the task; the key of its <see cref="AgentTaskEnded"/>.</param>
/// <param name="Kind">The agent's own word for what kind of work it is (the door states it, nothing here
/// interprets it); null when the door states none.</param>
/// <param name="Description">The agent's description of the work; null when the door states none.</param>
/// <param name="CallId">The <see cref="ToolCall.CallId"/> of the call that started it, which is also the
/// <see cref="ToolCall.ParentCallId"/> of a subagent's calls; null when the door states none.</param>
public sealed record AgentTaskStarted(string TaskId, string? Kind, string? Description, string? CallId) : AgentStreamEvent;

/// <summary>Background work ended, in the agent's word for how. It can arrive after the turn's
/// <see cref="SessionEnded"/>, which is never a second terminal.</summary>
/// <param name="TaskId">The <see cref="AgentTaskStarted.TaskId"/> it ends.</param>
/// <param name="Status">The agent's own word for how it ended (for example <c>completed</c> or <c>stopped</c>),
/// uninterpreted; null when the door states none.</param>
public sealed record AgentTaskEnded(string TaskId, string? Status) : AgentStreamEvent;

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
    string? FinalText, string? Diagnostic) : AgentStreamEvent
{
    /// <summary>What the failure was, for a run that failed: its kind, what it restricts and whether the prompt
    /// was delivered. Null when <see cref="IsError"/> is false. Both CLI doors set it on every failed
    /// terminal, read from the door's own fields before any sentence; <see cref="Verdict"/> is its mapping onto
    /// the verdict taxonomy, except that a process fault keeps the verdict its stderr gave.</summary>
    public AgentFailure? Failure { get; init; }

    /// <summary>What the turn showed after its prompt was written, which is what <see cref="AgentFailure.Delivery"/> is
    /// read from. Set by every door's loop on its terminal, failed or not; null on a terminal a door built before any
    /// turn ran (a refusal).</summary>
    public AgentTurnEvidence? Turn { get; init; }
}

/// <summary>The kinds of event a turn can show after its prompt was written. A session start the door announces
/// before it writes the prompt, and an event that is not the agent's answer to the prompt, are none of these.</summary>
public enum AgentShownKind { Text, Thinking, ToolCall, ToolResult, Usage, Plan, Task }

/// <summary>What one turn showed after its prompt was written: the evidence <see cref="AgentFailure.Delivery"/> is
/// read from, kept so a host can read it for itself.</summary>
/// <param name="Written">Whether the door saw the prompt's write complete: true or false where the door writes it
/// itself (the protocol door, which writes the prompt after its open), null where the child is handed the prompt at
/// its spawn and the door has no write to watch (the CLI doors).</param>
/// <param name="Shown">The kinds of event that came after the write, each once, in the order first shown; empty when
/// none did.</param>
public sealed record AgentTurnEvidence(bool? Written, IReadOnlyList<AgentShownKind> Shown);
