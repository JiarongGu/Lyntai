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
/// <para><b>How the inference is kept safe.</b> The tool mapping is SHAPE-driven, not name-driven: any item
/// whose type is not one of the three known message-ish types is surfaced as a tool step under codex's OWN
/// item-type name, with codex's OWN item object as the payload. Nothing is renamed, normalised or invented,
/// so a codex release that adds or renames a tool item still flows through, and a wrong guess costs
/// FEWER/less-pretty events rather than a wrong one. Two consequences a consumer should know:</para>
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

            // INFERRED: everything else is a step the agent took. codex's own item type is the tool name and
            // codex's own item object is the payload — nothing is renamed or normalised.
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

    /// <summary>INFERRED: whether a completed tool item reports a failure. Two signals are checked —
    /// a <c>status</c> of <c>failed</c>, and a non-zero <c>exit_code</c> — and neither being present means
    /// "not an error", so an unrecognised item shape reports success rather than a fabricated failure.</summary>
    private static bool IsFailedItem(JsonElement item)
    {
        if (CodexEnvelope.StringField(item, "status") is { } status &&
            status.Equals("failed", StringComparison.OrdinalIgnoreCase))
            return true;

        return item.TryGetProperty("exit_code", out var exit) && exit.ValueKind == JsonValueKind.Number &&
            exit.TryGetInt64(out var code) && code != 0;
    }
}
