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

/// <summary>What the agent says it started with, as its own start frame lists it (Claude's <c>system/init</c>): a reading of the
/// frame, never of what the host handed, so it can differ from the host's request. Yielded after the <see cref="SessionStarted"/>
/// of the same frame; a door whose start states none of the four lists yields none. A list the frame omits reads empty, and a
/// name the agent spells as a bare string or as an object's <c>name</c> reads the same. It says nothing of the prompt and never
/// counts toward its delivery.</summary>
/// <param name="Tools">The tools the agent says it has, in its order.</param>
/// <param name="McpServers">The MCP servers it lists, with the agent's own word for each one's state.</param>
/// <param name="Skills">The skills it lists.</param>
/// <param name="Plugins">The plugins it lists.</param>
public sealed record AgentLoaded(
    IReadOnlyList<string> Tools, IReadOnlyList<AgentLoadedServer> McpServers,
    IReadOnlyList<string> Skills, IReadOnlyList<string> Plugins) : AgentStreamEvent;

/// <summary>One MCP server an <see cref="AgentLoaded"/> lists.</summary>
/// <param name="Name">The server's name.</param>
/// <param name="Status">The agent's own word for its state (<c>connected</c>, <c>failed</c>…), uninterpreted; null when it states none.</param>
public sealed record AgentLoadedServer(string Name, string? Status);

/// <summary>A chunk of assistant answer text.</summary>
public sealed record TextDelta(string Text) : AgentStreamEvent
{
    /// <summary>The <see cref="ToolCall.CallId"/> of the call that spawned the subagent speaking; null for the session's
    /// own words, and for a door that does not say. A subagent's words arrive whole, one event per block.</summary>
    public string? ParentCallId { get; init; }

    /// <summary>The id of the agent's message these words belong to, as the frame that carried them states it (Claude's
    /// <c>message.id</c>), so a consumer can group a message's words; null where the door's wire states none. Never made up.</summary>
    public string? MessageId { get; init; }
}

/// <summary>A chunk of assistant reasoning/thinking text.</summary>
public sealed record Thinking(string Text) : AgentStreamEvent
{
    /// <summary>The <see cref="ToolCall.CallId"/> of the call that spawned the subagent thinking; null for the session's
    /// own, and for a door that does not say.</summary>
    public string? ParentCallId { get; init; }
}

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
public sealed record AgentTaskStarted(string TaskId, string? Kind, string? Description, string? CallId) : AgentStreamEvent
{
    /// <summary>The <see cref="TaskId"/> of the task that started this one, as the frame states it (a subagent's own child);
    /// null for work the session started and when the door states none.</summary>
    public string? ParentTaskId { get; init; }

    /// <summary>How deep this work sits below the session (the Claude door's <c>spawn_depth</c>, 1 for the session's own
    /// subagent); null when the door states none.</summary>
    public int? Depth { get; init; }

    /// <summary>The agent's own word for the subagent's type (Claude's <c>subagent_type</c>), uninterpreted; null for
    /// work that is no subagent and when the door states none.</summary>
    public string? SubagentType { get; init; }
}

/// <summary>Background work ended, in the agent's word for how. It can arrive after the turn's
/// <see cref="SessionEnded"/>, which is never a second terminal.</summary>
/// <param name="TaskId">The <see cref="AgentTaskStarted.TaskId"/> it ends.</param>
/// <param name="Status">The agent's own word for how it ended (for example <c>completed</c> or <c>stopped</c>),
/// uninterpreted; null when the door states none.</param>
public sealed record AgentTaskEnded(string TaskId, string? Status) : AgentStreamEvent
{
    /// <summary>The <see cref="AgentTaskStarted.CallId"/> of the call that started the work, so an end is keyed by the same
    /// call as its start: the frame's own, else the one its start stated; null when neither did.</summary>
    public string? CallId { get; init; }
}

/// <summary>A task's status as the agent patched it (the Claude door's <c>task_updated</c>), in the agent's own word. It is not
/// an ending: <see cref="AgentTaskEnded"/> is the agent's own end and is read from its own frame, so a task can be updated
/// <c>killed</c> and never ended when the agent's process is gone first. One per patch that states a status, in the order
/// the agent sent them, each possibly more than once for one task; a patch that states none yields nothing. It can arrive
/// after the turn's <see cref="SessionEnded"/>.</summary>
/// <param name="TaskId">The <see cref="AgentTaskStarted.TaskId"/> it updates.</param>
/// <param name="Status">The agent's own word (for example <c>completed</c> or <c>killed</c>), uninterpreted.</param>
public sealed record AgentTaskUpdated(string TaskId, string Status) : AgentStreamEvent;

/// <summary>The file a task's output is written to, as a frame names it: a path on the agent's machine, never the output
/// itself. The library reads no file, so a host that wants the stream reads it from here. Yielded once per task and path,
/// at the frame that first names it: a task frame's own field, or (the Claude door) a backgrounded command's result, which
/// names it while the command runs; it may be the task's end.</summary>
/// <param name="TaskId">The <see cref="AgentTaskStarted.TaskId"/> the file belongs to.</param>
/// <param name="Path">The path as the frame spells it.</param>
public sealed record AgentTaskOutputFile(string TaskId, string Path) : AgentStreamEvent;

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

/// <summary>An allowance report the agent stated mid-turn (Claude's <c>rate_limit_event</c>, allowed or rejected), as it
/// stated it. A turn that fails on it still ends as before; this is the report itself, for a host that reads an allowance
/// as it moves. It says nothing of the prompt and never counts toward its delivery.</summary>
/// <param name="Status">The report's standing in the agent's own word (<c>allowed</c>, <c>rejected</c>…); null when it stated none.</param>
/// <param name="Window">The window it names, as <see cref="AgentAllowanceWindow"/> states one: <see cref="AgentAllowanceWindow.FiveHour"/>
/// and <see cref="AgentAllowanceWindow.Weekly"/> with their length and a scope for a model's week, any other window by the agent's
/// own word with no length. Its <see cref="AgentAllowanceWindow.UsedPercent"/> is the window's use when the report states one as a
/// fraction from 0 to 1 (the window's own entry in the report's windows, else the report's own), as a percent with its
/// <see cref="AgentAllowanceWindow.UsedStep"/>; null when it states none or one out of range. Null when the report names no window.</param>
/// <param name="LimitScope">The spend limit the report names when it is not the member's own (<c>group_pool</c>), in the agent's word.</param>
/// <param name="IsUsingOverage">Whether the agent states the account is drawing on overage; null when it states none.</param>
/// <param name="OverageResetsAt">When the agent states the overage renews; null when it states none.</param>
public sealed record AgentAllowanceReport(
    string? Status, AgentAllowanceWindow? Window, string? LimitScope, bool? IsUsingOverage, DateTimeOffset? OverageResetsAt)
    : AgentStreamEvent
{
    /// <summary>The agent's word for whether overage is open to the account (<c>allowed</c>, <c>rejected</c>…), as written; null
    /// when it states none.</summary>
    public string? OverageStatus { get; init; }

    /// <summary>The agent's word for why overage is closed (<c>member_zero_credit_limit</c>…), as written; null when it states none.</summary>
    public string? OverageDisabledReason { get; init; }
}

/// <summary>One option an agent offered when it opened the session (the protocol door's config option).</summary>
/// <param name="Category">The agent's category word for it (<c>model</c>, <c>thought_level</c>…); null when it stated none.</param>
/// <param name="Id">What the option is set by.</param>
/// <param name="Current">The value it holds now; null when the agent stated none.</param>
/// <param name="Values">The values it may be set to, grouped choices flattened, in the agent's order, at most 25.</param>
public sealed record AgentOfferedOption(string? Category, string Id, string? Current, IReadOnlyList<string> Values);

/// <summary>One mode an agent offered when it opened the session.</summary>
/// <param name="Id">What <c>session/set_mode</c> names it by.</param>
/// <param name="Name">The agent's name for it; null when it stated none.</param>
public sealed record AgentOfferedMode(string Id, string? Name);

/// <summary>What the protocol door's agent offered in its open answer: yielded after the open and before any set or the
/// prompt, on a turn that is then refused too, so a host can say what the agent would have taken. It is not the agent's
/// answer to the prompt and never counts toward a prompt's delivery. Every list is at most 25 long, and nothing is inferred.</summary>
/// <param name="Options">The config options, at most 25.</param>
/// <param name="Modes">The modes, at most 25.</param>
/// <param name="CurrentModeId">The mode the agent is in; null when it stated none.</param>
/// <param name="Truncated">Whether any list was cut to its bound.</param>
public sealed record AgentOffers(
    IReadOnlyList<AgentOfferedOption> Options, IReadOnlyList<AgentOfferedMode> Modes, string? CurrentModeId, bool Truncated)
    : AgentStreamEvent;

/// <summary>A tool call the agent's harness denied, as the turn's own report states it (Claude's
/// <c>permission_denials</c>): the turn went on without the tool's result.</summary>
/// <param name="Tool">The agent's name for the tool.</param>
/// <param name="CallId">The <see cref="ToolCall.CallId"/> of the denied call; null when the report names none.</param>
/// <param name="Reason">The reason, where the report's field gives one; null otherwise, never guessed.</param>
public sealed record AgentToolDenied(string Tool, string? CallId, string? Reason) : AgentStreamEvent;

/// <summary>One model's share of a turn, as the agent's own report states it. A count the report did not send is null.</summary>
/// <param name="Model">The model id as the report spells it: the session's, a subagent's, or one the harness used for itself.</param>
/// <param name="InputTokens">Input count, null = not reported.</param>
/// <param name="OutputTokens">Output count, null = not reported.</param>
/// <param name="CacheReadTokens">Count served from a cache, null = not reported.</param>
/// <param name="CacheCreateTokens">Count written to a cache, null = not reported.</param>
/// <param name="ContextWindow">The model's context window in tokens, null = not reported.</param>
/// <param name="IsSessionModel">Whether this is the model the session ran on (<see cref="UsageFinal.Model"/>); the other
/// rows are what a subagent or the harness used, left as the rows they are.</param>
public sealed record AgentModelUsageEntry(
    string Model, long? InputTokens, long? OutputTokens, long? CacheReadTokens, long? CacheCreateTokens, long? ContextWindow,
    bool IsSessionModel);

/// <summary>A turn's usage split by model, as the agent's own report states it (Claude's <c>modelUsage</c>), beside the
/// totals of <see cref="UsageReading"/>. No cost is carried: the host prices from its own table. Absent when the
/// report carries none.</summary>
/// <param name="Models">One entry per model the report lists, in its order.</param>
/// <param name="MeasuredAt">When the library read the report off the wire, on its own clock.</param>
public sealed record AgentModelUsage(IReadOnlyList<AgentModelUsageEntry> Models, DateTimeOffset MeasuredAt) : AgentStreamEvent;

/// <summary>How full the agent's context window is, as the agent reported it mid-turn (the protocol door's
/// <c>usage_update</c>). These are context figures, not the token counts of a <see cref="UsageReading"/>, and each is the
/// agent's own. A figure the agent did not send is null: never inferred from the other, and never 0.</summary>
/// <param name="UsedTokens">What the context holds now.</param>
/// <param name="WindowTokens">The context window's size.</param>
/// <param name="MeasuredAt">When the library read the figures off the door's wire, on its own clock.</param>
public sealed record ContextReading(long? UsedTokens, long? WindowTokens, DateTimeOffset MeasuredAt) : AgentStreamEvent;

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

    /// <summary>The frames of the turn that no reader case took, by type and subtype (see <see cref="AgentUnreadFrame"/>):
    /// what the door read nothing from, so a frame it turned into an event is not here. At most 16 entries, empty when
    /// the door read every frame it was sent. The readers stay tolerant and add no event for these.</summary>
    public IReadOnlyList<AgentUnreadFrame> Unread { get; init; } = [];
}

/// <summary>One kind of frame a door's reader read nothing from.</summary>
/// <param name="Type">The frame's own type word (a stream-json type, a JSON-RPC method, a session update's kind).</param>
/// <param name="Subtype">Its subtype where the wire has one, else null.</param>
/// <param name="Count">How many such frames the turn carried, exact.</param>
/// <param name="Sample">The first such frame as written, cut to 512 characters. It is the agent's own text and may hold
/// what the agent was asked or answered: a host that logs it decides who may read the log.</param>
public sealed record AgentUnreadFrame(string Type, string? Subtype, int Count, string Sample);

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
