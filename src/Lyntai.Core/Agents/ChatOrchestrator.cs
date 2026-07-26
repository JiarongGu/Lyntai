using Lyntai.Cortex;
using Lyntai.Guards;
using Lyntai.Llm;
using Lyntai.Memory;
using Lyntai.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Agents;

/// <inheritdoc cref="IChatOrchestrator"/>
public sealed class ChatOrchestrator(
    ILlmClient llm,
    IToolLoop toolLoop,
    IToolRegistry tools,
    IGuardRail guards,
    IPromptComposer composer,
    IMemoryStore? memory = null,
    ISemanticMemory? semantic = null,
    ILogger<ChatOrchestrator>? logger = null) : IChatOrchestrator
{
    private readonly ILogger _logger = logger ?? NullLogger<ChatOrchestrator>.Instance;

    public async Task<ChatResult> ChatAsync(ChatTurn turn, CancellationToken ct = default)
    {
        // GATE 1 — input, on the RAW user message (BEFORE memory composition): a Replace then maps 1:1 to
        // the text we run AND remember. Gating the composed prompt used to persist the whole redacted
        // COMPOSED text — re-storing the recalled facts as a new record every Replace turn (compounding
        // growth). Recalled memory is deliberately not re-inspected here: it was already gated when stored.
        var messages = new List<LlmMessage>();
        if (!string.IsNullOrEmpty(turn.System)) messages.Add(LlmMessage.System(turn.System));
        messages.Add(LlmMessage.User(turn.Message));

        var pre = await guards.InspectRequestAsync(
            new LlmRequest { Messages = messages, Consumer = turn.Consumer }, ct).ConfigureAwait(false);
        if (pre.Result == GuardOutcome.Kind.Block)
            return new ChatResult("", LlmVerdict.Refused, Blocked: true, pre.Reason, []);
        // what we persist to memory: the REDACTED text when the gate rewrote it (never re-store the raw
        // input a redaction guard just removed — that would re-inject the secret on the next recall)
        var rememberedQuestion = pre.Result == GuardOutcome.Kind.Replace ? pre.Replacement! : turn.Message;

        // recall task-scoped memory into the (possibly rewritten) message (fail-open; no TaskKey → as-is)
        var userText = turn.TaskKey is null
            ? rememberedQuestion
            : await composer.ComposeAsync(rememberedQuestion, turn.TaskKey, turn.MemoryScope, rememberedQuestion, ct: ct).ConfigureAwait(false);
        var req = new LlmRequest { Messages = [.. messages[..^1], LlmMessage.User(userText)], Consumer = turn.Consumer };

        // run: the tool loop (model can call tools) or a plain completion
        string answer;
        LlmVerdict verdict;
        string? detail;
        IReadOnlyList<ToolStep> steps;
        if (turn.UseTools && tools.Tools.Count > 0)
        {
            var result = await toolLoop.RunAsync(req, ct: ct).ConfigureAwait(false);
            (answer, verdict, detail, steps) = (result.Answer, result.Verdict, result.Detail, result.Steps);
        }
        else
        {
            var reply = await llm.CompleteAsync(req, ct).ConfigureAwait(false);
            (answer, verdict, detail, steps) = (reply.Text, reply.Verdict, reply.Detail, []);
        }
        if (verdict != LlmVerdict.Ok)
            return new ChatResult("", verdict, Blocked: false, detail, steps);

        // GATE 2 — output
        var post = await guards.InspectResponseAsync(new LlmReply(answer, LlmVerdict.Ok), ct).ConfigureAwait(false);
        if (post.Result == GuardOutcome.Kind.Block)
            return new ChatResult("", LlmVerdict.Refused, Blocked: true, post.Reason, steps);
        if (post.Result == GuardOutcome.Kind.Replace)
            answer = post.Replacement!;

        // remember the exchange into BOTH memory sources that are wired (fail-open — a memory outage never
        // breaks the chat; the composer reads them back as a hybrid recall on the next turn)
        if (turn.Remember && turn.TaskKey is not null)
        {
            var record = $"Q: {rememberedQuestion}\nA: {answer}";
            if (memory is not null)
            {
                try { await memory.RememberAsync(turn.TaskKey, turn.MemoryScope, record, ct: ct).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "chat: lexical memory write failed (non-fatal)"); }
            }
            if (semantic is not null)
            {
                try { await semantic.RememberAsync(turn.TaskKey, turn.MemoryScope, record, ct).ConfigureAwait(false); }
                catch (Exception ex) { _logger.LogWarning(ex, "chat: semantic memory write failed (non-fatal)"); }
            }
        }

        return new ChatResult(answer, LlmVerdict.Ok, Blocked: false, null, steps);
    }
}
