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
        // recall task-scoped memory into the user message (fail-open; no TaskKey → the message as-is)
        var userText = turn.TaskKey is null
            ? turn.Message
            : await composer.ComposeAsync(turn.Message, turn.TaskKey, turn.MemoryScope, turn.Message, ct: ct).ConfigureAwait(false);

        var messages = new List<LlmMessage>();
        if (!string.IsNullOrEmpty(turn.System)) messages.Add(LlmMessage.System(turn.System));
        messages.Add(LlmMessage.User(userText));
        var req = new LlmRequest { Messages = messages, Consumer = turn.Consumer };

        // GATE 1 — input
        var pre = await guards.InspectRequestAsync(req, ct).ConfigureAwait(false);
        if (pre.Result == GuardOutcome.Kind.Block)
            return new ChatResult("", LlmVerdict.Refused, Blocked: true, pre.Reason, []);
        // what we persist to memory: the REDACTED text when the gate rewrote it (never re-store the raw
        // input a redaction guard just removed — that would re-inject the secret on the next recall)
        var rememberedQuestion = turn.Message;
        if (pre.Result == GuardOutcome.Kind.Replace)
        {
            req = req with { Messages = [.. messages[..^1], LlmMessage.User(pre.Replacement!)] };
            rememberedQuestion = pre.Replacement!;
        }

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
