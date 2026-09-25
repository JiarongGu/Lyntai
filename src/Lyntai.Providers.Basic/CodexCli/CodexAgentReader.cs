using Lyntai.Inference;
using System.Text;
using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Providers.Basic;

namespace Lyntai.Providers.CodexCli;

/// <summary>Stateful, per-run translator: feed it each <c>codex exec --json</c> line via <see cref="Read"/>
/// and it yields 0..N <see cref="AgentStreamEvent"/>s. Tolerant — an unknown or malformed line yields
/// nothing, never throws. Line-translation ONLY: it has no stderr knowledge, so the session runner fills
/// <see cref="SessionEnded.Diagnostic"/> for process-level faults.
/// <para><b>MEASURED, envelope and tool steps alike</b> (codex-cli 0.146.0 and re-measured against 0.155.1
/// — <c>docs/DECISIONS.md</c> D35, <c>docs/task-archive.md</c> Part 260). Each <see cref="CodexEnvelope"/>
/// member says which release measured it.</para>
/// <para><b>The mapping is SHAPE-driven, not name-driven.</b> Any item whose type is not one of the three
/// recognised message-ish names (<c>agent_message</c>, <c>reasoning</c>, <c>error</c>) is surfaced as a tool
/// step under codex's OWN item-type name, carrying codex's OWN item object — nothing renamed or invented, so
/// <see cref="ToolCall.ArgumentsJson"/> / <see cref="ToolResult.Content"/> hold that raw object. Where codex
/// emits no <c>item.started</c> the <see cref="ToolCall"/> is SYNTHESISED from the completion, correlated by
/// id and never emitted twice; 0.155.1 emits <c>item.started</c> for every tool item, so that is the
/// fallback it was designed as.</para>
/// <para><b>One residual limit</b>: the default arm is reached by ELIMINATION, so a hypothetical non-tool
/// item outside those three names would appear as a tool step — none was observed on either build. Treat a
/// tool step's KIND as provisional and its PAYLOAD as reliable; switch on <see cref="ToolCall.Name"/>.</para>
/// <para><b>The rule that is measured and load-bearing:</b> only <c>turn.failed</c> is terminal. A bare
/// <c>error</c> line and an <c>error</c> ITEM both appeared in a run that went on to SUCCEED, so failing on
/// either is wrong.</para></summary>
internal sealed class CodexAgentReader
{
    private readonly HashSet<string> _startedItems = new(StringComparer.Ordinal);
    private readonly StringBuilder _answer = new();
    private string? _threadId;

    /// <summary>The session id seen so far (<c>thread.started</c>), for a terminal the runner has to
    /// fabricate after a process-level fault.</summary>
    public string? ThreadId => _threadId;

    /// <summary>Translates one JSONL line into 0..N events. Never throws.</summary>
    public IReadOnlyList<AgentStreamEvent> Read(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return [];
        try
        {
            // materialized inside the using: every element read belongs to the document
            using var doc = JsonDocument.Parse(line);
            return [.. ReadLine(doc.RootElement)];
        }
        catch (Exception ex) when (WireJson.IsShapeFault(ex))
        {
            return [];   // codex interleaves plain-text tracing lines with the JSONL
        }
    }

    private IEnumerable<AgentStreamEvent> ReadLine(JsonElement root)
    {
        switch (CodexEnvelope.Type(root))
        {
            case CodexEnvelope.ThreadStarted:
                if (WireJson.String(root, "thread_id") is { Length: > 0 } threadId)
                {
                    _threadId = threadId;
                    yield return new SessionStarted(threadId);
                }
                break;

            case CodexEnvelope.ItemStarted:
                foreach (var e in ReadItem(root, started: true)) yield return e;
                break;

            case CodexEnvelope.ItemCompleted:
                foreach (var e in ReadItem(root, started: false)) yield return e;
                break;

            case CodexEnvelope.TurnCompleted:
                foreach (var e in ReadTurnCompleted(root)) yield return e;
                break;

            case CodexEnvelope.TurnFailed:
                var message = CodexEnvelope.FailureMessage(root);
                yield return new SessionEnded(
                    Verdict: ProviderVerdictClassifier.FromErrorText(message),
                    IsError: true,
                    Subtype: null,                 // codex reports no failure subtype
                    SessionId: _threadId,
                    FinalText: null,               // a failed turn's partial text is not an answer
                    Diagnostic: message);
                break;

            // Everything else — `turn.started`, a bare `error` NOTICE, `item.updated` (a partial whose
            // accumulation rule is unmeasured, so counting it would risk duplicating the answer), and
            // any future envelope type — is deliberately ignored.
        }
    }

    /// <summary>MEASURED: <c>turn.completed</c> carries usage and NO text, so the answer is whatever the
    /// preceding <c>agent_message</c> items said. Usage is yielded BEFORE the terminal so the fold in
    /// <see cref="AgentSessionExtensions.RunAsync"/> has it.</summary>
    private IEnumerable<AgentStreamEvent> ReadTurnCompleted(JsonElement root)
    {
        if (CodexEnvelope.ReadUsage(root) is { } usage)
        {
            // Model is null, not guessed: codex's thread events carry no model id, and echoing back the
            // model the CALLER asked for would report a request as an observation (the CLI may have
            // substituted one, and with no --model it picks its own).
            yield return new UsageFinal(usage.Input, usage.Output, usage.CacheRead, usage.CacheCreate, Model: null);
        }

        yield return new SessionEnded(
            Verdict: ProviderVerdict.Ok,
            IsError: false,
            Subtype: null,
            SessionId: _threadId,
            FinalText: _answer.Length > 0 ? _answer.ToString() : null,
            Diagnostic: null);
    }

    /// <summary>One <c>item.started</c> / <c>item.completed</c> line.</summary>
    private IEnumerable<AgentStreamEvent> ReadItem(JsonElement root, bool started)
    {
        if (CodexEnvelope.Item(root) is not { } item) yield break;
        if (WireJson.String(item, "type") is not { Length: > 0 } itemType) yield break;

        var id = WireJson.String(item, "id");

        switch (itemType)
        {
            // MEASURED. The text arrives whole at completion — codex's thread events carry no token-level
            // deltas, so a TextDelta here is one complete assistant message, not a token. Accumulated the
            // same way the codex PROVIDER accumulates content, so RunAsync and CompleteAsync agree.
            case CodexEnvelope.AgentMessageItem:
                if (!started && WireJson.String(item, "text") is { Length: > 0 } text)
                {
                    _answer.Append(text);
                    yield return new TextDelta(text);
                }
                break;

            // MEASURED (codex 0.155.1): the item type is `reasoning` — not `agent_reasoning` — and the
            // text field is `text`, both confirmed on a real turn (D35 re-measurement).
            case CodexEnvelope.ReasoningItem:
                if (!started && WireJson.String(item, "text") is { Length: > 0 } thought)
                    yield return new Thinking(thought);
                break;

            // MEASURED: a NON-terminal warning, present in a run that succeeded. There is no
            // AgentStreamEvent case for "a notice", and inventing a terminal here is the defect this
            // backend exists to avoid, so it is dropped.
            case CodexEnvelope.ErrorItem:
                break;

            // MEASURED shapes reach this arm by ELIMINATION rather than recognition: everything that is not
            // one of the three names above is a step the agent took, under codex's own item type and carrying
            // codex's own item object. codex 0.155.1 confirmed four here — `command_execution` (shell),
            // `file_change` (edit), `mcp_tool_call`, `web_search` — all genuine tools, so elimination gave
            // the right KIND for each. See the class docblock's "one residual limit".
            default:
                var payload = item.GetRawText();
                if (started)
                {
                    if (id is { Length: > 0 }) _startedItems.Add(id);
                    yield return new ToolCall(itemType, payload, id);
                }
                else
                {
                    if (id is not { Length: > 0 } || !_startedItems.Contains(id))
                        yield return new ToolCall(itemType, payload, id);   // synthesised: the step is still visible
                    yield return new ToolResult(id, payload, IsFailedItem(item));
                }
                break;
        }
    }

    /// <summary>MEASURED (codex 0.155.1): whether a completed tool item reports a failure. Two TOP-LEVEL
    /// signals are checked — a <c>status</c> of <c>failed</c>, and a non-zero <c>exit_code</c> — and both are
    /// exactly what a real <c>command_execution</c> emits: a failed run carried <c>"status":"failed"</c> AND
    /// <c>"exit_code":1</c> together, a successful one <c>"completed"</c> AND <c>0</c>, both at top level and
    /// in agreement. The inferred pair was right — not nested, not differently named (D35 re-measurement).
    /// A <c>file_change</c> carries no <c>exit_code</c>, so its failure would ride <c>status</c> alone, which
    /// this already reads. Neither signal present means "not an error".
    /// <para><b>Note which way an UNMEASURED shape errs.</b> <c>false</c> here is not "unknown", it is a
    /// positive claim of success on <see cref="ToolResult.IsError"/> — so a failure signal that is NESTED or
    /// differently named on some future item is reported as a successful step. That direction was chosen
    /// because the opposite (defaulting to <c>IsError: true</c>) would mark every successful step of an
    /// unmeasured item shape as failed, which is both wrong more often and louder. The raw item is in
    /// <see cref="ToolResult.Content"/> either way.</para></summary>
    private static bool IsFailedItem(JsonElement item)
    {
        if (WireJson.String(item, "status") is { } status &&
            status.Equals("failed", StringComparison.OrdinalIgnoreCase))
            return true;

        return item.TryGetProperty("exit_code", out var exit) && exit.ValueKind == JsonValueKind.Number &&
            exit.TryGetInt64(out var code) && code != 0;
    }
}
