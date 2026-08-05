using System.Text;
using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Llm;

namespace Lyntai.Providers.CodexCli;

/// <summary>Stateful, per-run translator: feed it each <c>codex exec --json</c> line via <see cref="Read"/>
/// and it yields 0..N <see cref="AgentStreamEvent"/>s. Tolerant — an unknown or malformed line yields
/// nothing, never throws. Line-translation ONLY: it has no stderr knowledge, so the session runner fills
/// <see cref="SessionEnded.Diagnostic"/> for process-level faults.
///
/// <para><b>What is measured and what is not.</b> The envelope this reads was captured from codex-cli
/// 0.146.0 on 2026-08-04 (see <see cref="CodexJsonlParser"/> for the capture), but that run used NO TOOLS —
/// so the half of this mapping that carries tool steps, which is the whole reason the agent-session shape
/// exists, is INFERRED. Each member of <see cref="CodexEnvelope"/> says which it is.</para>
///
/// <para><b>How the inference is bounded.</b> The tool mapping is SHAPE-driven, not name-driven: any item
/// whose type is not one of the three recognised message-ish names (<c>agent_message</c>, <c>reasoning</c>,
/// <c>error</c>) is surfaced as a tool step under codex's OWN item-type name, with codex's OWN item object as
/// the payload. Nothing is renamed, normalised or invented, so a codex release that adds or renames a TOOL
/// item still flows through. Two consequences a consumer should know:</para>
/// <list type="bullet">
/// <item><see cref="ToolCall.ArgumentsJson"/> and <see cref="ToolResult.Content"/> are the raw codex item
///   object, not a normalised argument schema. Read the fields you care about from it. There is deliberately
///   no <c>CodexToolCalls</c> helper (the claude twin of which parses measured argument names) — inventing
///   one would mean guessing codex's per-item field names.</item>
/// <item>Where codex emits no <c>item.started</c> for a tool item, the <see cref="ToolCall"/> is SYNTHESISED
///   from the completion so the step is still visible in a UI, correlated to its
///   <see cref="ToolResult"/> by the item id. It is never emitted twice for the same id.</item>
/// </list>
///
/// <para><b>The limit of that guarantee — read this before trusting a tool step.</b> What the shape-driven
/// mapping actually guarantees is narrow: <b>no payload is ever invented or dropped</b> (codex's item object
/// is passed through verbatim), and every uncertainty is <b>confined to the tool-step half</b> — the session
/// id, the terminal and the usage counts are measured and unaffected. It does NOT guarantee that every event
/// is the RIGHT KIND of event, because the default arm is reached by ELIMINATION against three guessed-or-
/// measured names. A non-tool item whose name is not one of those three <b>will appear as a tool step</b>,
/// which contradicts <see cref="ToolCall"/>'s own contract ("the agent invoked a tool"). Three ways that
/// happens, all consistent with what is known today:</para>
/// <list type="bullet">
/// <item><c>reasoning</c> is itself INFERRED. If codex names it <c>agent_reasoning</c> (its historical name)
///   the model's reasoning arrives as a fabricated <see cref="ToolCall"/> whose arguments are the thought.</item>
/// <item>An item that is neither a message nor a tool — a plan/<c>todo_list</c> update, say — surfaces as a
///   tool step for the same reason.</item>
/// <item>Worst case, a rename of <c>agent_message</c> would cost the <see cref="TextDelta"/> AND
///   <see cref="SessionEnded.FinalText"/> and emit the answer itself as a tool step.</item>
/// </list>
/// <para>So: treat a tool step's KIND as provisional and its PAYLOAD as reliable, and prefer switching on
/// <see cref="ToolCall.Name"/> (codex's own item type) over assuming every one is a tool. Confirming the four
/// names this turns on is the first item of task CLI12 in <c>TASKS.md</c>.</para>
///
/// <para><b>The rule that is measured and load-bearing:</b> only <c>turn.failed</c> is terminal. A bare
/// <c>error</c> line and an <c>error</c> ITEM both appeared in the run that went on to SUCCEED, and failing
/// on either is the exact defect a consuming app's hand-rolled parser shipped.</para></summary>
internal sealed class CodexAgentReader
{
    private readonly HashSet<string> _startedItems = new(StringComparer.Ordinal);
    private readonly StringBuilder _answer = new();
    private string? _threadId;

    /// <summary>The session id seen so far (<c>thread.started</c>), for a terminal the runner has to
    /// fabricate after a process-level fault.</summary>
    public string? ThreadId => _threadId;

    /// <summary>Translates one JSONL line into 0..N events. Never throws.</summary>
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
            yield break;   // codex interleaves plain-text tracing lines with the JSONL
        }

        using (doc)
        {
            var root = doc.RootElement;
            switch (CodexEnvelope.Type(root))
            {
                case CodexEnvelope.ThreadStarted:
                    if (CodexEnvelope.StringField(root, "thread_id") is { Length: > 0 } threadId)
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
                        Verdict: LlmVerdictClassifier.FromErrorText(message),
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
            Verdict: LlmVerdict.Ok,
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
        if (CodexEnvelope.StringField(item, "type") is not { Length: > 0 } itemType) yield break;

        var id = CodexEnvelope.StringField(item, "id");

        switch (itemType)
        {
            // MEASURED. The text arrives whole at completion — codex's thread events carry no token-level
            // deltas, so a TextDelta here is one complete assistant message, not a token. Accumulated the
            // same way the codex PROVIDER accumulates content, so RunAsync and CompleteAsync agree.
            case CodexEnvelope.AgentMessageItem:
                if (!started && CodexEnvelope.StringField(item, "text") is { Length: > 0 } text)
                {
                    _answer.Append(text);
                    yield return new TextDelta(text);
                }
                break;

            // INFERRED (the item type name and its text field alike).
            case CodexEnvelope.ReasoningItem:
                if (!started && CodexEnvelope.StringField(item, "text") is { Length: > 0 } thought)
                    yield return new Thinking(thought);
                break;

            // MEASURED: a NON-terminal warning, present in a run that succeeded. There is no
            // AgentStreamEvent case for "a notice", and inventing a terminal here is the defect this
            // backend exists to avoid, so it is dropped.
            case CodexEnvelope.ErrorItem:
                break;

            // INFERRED, and reached by ELIMINATION rather than recognition: everything that is not one of the
            // three names above is ASSUMED to be a step the agent took. codex's own item type is the tool name
            // and codex's own item object is the payload — nothing is renamed or normalised, so no payload is
            // ever invented. But the KIND is a guess: a non-tool item whose name we don't recognise (a renamed
            // `reasoning`, a `todo_list` plan update) arrives here and is reported as a tool invocation, which
            // ToolCall's own contract says it is not. See the class docblock's "limit of that guarantee".
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

    /// <summary>INFERRED: whether a completed tool item reports a failure. Two TOP-LEVEL signals are checked —
    /// a <c>status</c> of <c>failed</c>, and a non-zero <c>exit_code</c> — and neither being present means
    /// "not an error", so an unrecognised item shape reports success rather than a fabricated failure.
    /// <para><b>Note which way that errs.</b> <c>false</c> here is not "unknown", it is a positive claim of
    /// success on <see cref="ToolResult.IsError"/> — so a failure signal that is NESTED or differently named
    /// is reported as a successful step, and a UI that highlights failures would show none. That direction was
    /// chosen because the opposite (defaulting to <c>IsError: true</c>) would mark every successful step of an
    /// unmeasured item shape as failed, which is both wrong more often and louder. Confirming the real signal
    /// is part of task CLI12; the raw item is in <see cref="ToolResult.Content"/> either way.</para></summary>
    private static bool IsFailedItem(JsonElement item)
    {
        if (CodexEnvelope.StringField(item, "status") is { } status &&
            status.Equals("failed", StringComparison.OrdinalIgnoreCase))
            return true;

        return item.TryGetProperty("exit_code", out var exit) && exit.ValueKind == JsonValueKind.Number &&
            exit.TryGetInt64(out var code) && code != 0;
    }
}
