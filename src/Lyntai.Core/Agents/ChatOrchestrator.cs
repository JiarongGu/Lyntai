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
        // GATE 1a — input, on the RAW user message (BEFORE memory composition): a Replace then maps 1:1 to
        // the text we run AND remember. Gating only the composed prompt used to persist the whole redacted
        // COMPOSED text — re-storing the recalled facts as a new record every Replace turn (compounding growth).
        var messages = new List<LlmMessage>();
        if (!string.IsNullOrEmpty(turn.System)) messages.Add(LlmMessage.System(turn.System));
        messages.Add(LlmMessage.User(turn.Message));

        var pre = await guards.InspectRequestAsync(
            new LlmRequest { Messages = messages, Consumer = turn.Consumer }, ct).ConfigureAwait(false);
        if (pre.Result == GuardOutcome.Kind.Block)
            // no Usage on either gate-1 exit, deliberately: the turn never reached a provider, and an
            // all-zero figure would read as "the model answered for free" (see ChatResult.Usage).
            return new ChatResult("", LlmVerdict.Refused, Blocked: true, pre.Reason, []);
        // what we persist to memory: the REDACTED text when the gate rewrote it (never re-store the raw
        // input a redaction guard just removed — that would re-inject the secret on the next recall)
        var rememberedQuestion = pre.Result == GuardOutcome.Kind.Replace ? pre.Replacement! : turn.Message;

        // recall task-scoped memory into the (possibly rewritten) message (fail-open; no TaskKey → as-is)
        var userText = turn.TaskKey is null
            ? rememberedQuestion
            : await composer.ComposeAsync(rememberedQuestion, turn.TaskKey, turn.MemoryScope, rememberedQuestion, ct: ct).ConfigureAwait(false);
        var req = new LlmRequest { Messages = [.. messages[..^1], LlmMessage.User(userText)], Consumer = turn.Consumer };

        // GATE 1b — the COMPOSED prompt, when composition actually added recalled memory: facts written
        // through the PUBLIC memory seams (or before a guard existed) were never input-gated, so the full
        // outbound prompt must pass the gate too. A Block refuses the turn; a Replace redacts what the
        // MODEL sees — the remembered question stays the 1a result (recalled facts are never re-persisted).
        if (!ReferenceEquals(userText, rememberedQuestion) && userText != rememberedQuestion)
        {
            var preComposed = await guards.InspectRequestAsync(req, ct).ConfigureAwait(false);
            if (preComposed.Result == GuardOutcome.Kind.Block)
                return new ChatResult("", LlmVerdict.Refused, Blocked: true, preComposed.Reason, []);
            if (preComposed.Result == GuardOutcome.Kind.Replace)
                req = req with { Messages = [.. messages[..^1], LlmMessage.User(preComposed.Replacement!)] };
        }

        // run: the tool loop (model can call tools) or a plain completion. `usage` is carried out of BOTH
        // arms and onto every remaining exit: the tokens were spent whatever the turn then does with them,
        // and the loop already summed its own — dropping it made a chat consumer wrap ILlmClient in a
        // front-door decorator to recompute a figure the loop had handed us.
        string answer;
        LlmVerdict verdict;
        string? detail;
        IReadOnlyList<ToolStep> steps;
        LlmUsage? usage;
        if (turn.UseTools && tools.Tools.Count > 0)
        {
            var result = await toolLoop.RunAsync(req, ct: ct).ConfigureAwait(false);
            (answer, verdict, detail, steps, usage) = (result.Answer, result.Verdict, result.Detail, result.Steps, result.Usage);
        }
        else
        {
            var reply = await llm.CompleteAsync(req, ct).ConfigureAwait(false);
            (answer, verdict, detail, steps, usage) = (reply.Text, reply.Verdict, reply.Detail, [], reply.Usage);
        }
        if (verdict != LlmVerdict.Ok)
            return new ChatResult("", verdict, Blocked: false, detail, steps) { Usage = usage };

        // GATE 2 — output
        var post = await guards.InspectResponseAsync(new LlmReply(answer, LlmVerdict.Ok), ct).ConfigureAwait(false);
        if (post.Result == GuardOutcome.Kind.Block)
            return new ChatResult("", LlmVerdict.Refused, Blocked: true, post.Reason, steps) { Usage = usage };
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

        return new ChatResult(answer, LlmVerdict.Ok, Blocked: false, null, steps) { Usage = usage };
    }
}
