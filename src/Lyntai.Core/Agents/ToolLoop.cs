using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Lyntai.Diagnostics;
using Lyntai.Guards;
using Lyntai.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Agents;

/// <summary>
/// Default <see cref="IToolLoop"/>. Uses <b>native</b> tool-calling when the routing supports it
/// (<see cref="ILlmClient.SupportsToolCalls"/>): tool declarations go to the model and its structured
/// <see cref="LlmReply.ToolCalls"/> drive execution, with results fed back as tool-role messages.
/// Otherwise it falls back to a provider-agnostic <b>prompt protocol</b> over the text contract (the
/// model replies with one JSON object, <c>{"tool":…}</c> or <c>{"final":…}</c>, via
/// <see cref="LlmStructuredExtensions.CompleteJsonAsync"/>). Both paths execute the same registered
/// <see cref="ITool"/>s. Unknown tools and tools that throw become <c>error: …</c> observations fed back
/// to the model (it can recover) rather than exceptions; a non-Ok LLM verdict is surfaced as-is.
/// </summary>
public sealed class ToolLoop(
    ILlmClient client,
    IToolRegistry registry,
    LyntaiOptions options,
    ILogger<ToolLoop>? logger = null,
    IGuardRail? guards = null) : IToolLoop
{
    private readonly ILogger _logger = logger ?? NullLogger<ToolLoop>.Instance;

    /// <summary>Result door: drives the shared core to completion and folds its event stream into a
    /// <see cref="ToolLoopResult"/> (steps + usage are populated as the core runs; the terminal
    /// <see cref="SessionEnded"/> carries the answer/verdict/detail).</summary>
    public async Task<ToolLoopResult> RunAsync(LlmRequest req, int? maxIterations = null, CancellationToken ct = default)
    {
        var steps = new List<ToolStep>();
        var usage = new UsageSum();
        SessionEnded? end = null;
        await foreach (var e in RunCoreAsync(req, maxIterations, steps, usage, ct).ConfigureAwait(false))
            if (e is SessionEnded se) end = se; // the core always terminates with exactly one SessionEnded

        var verdict = end?.Verdict ?? LlmVerdict.Failed;
        return new ToolLoopResult(end?.FinalText ?? "", verdict, steps, end?.Diagnostic) { Usage = usage.Value };
    }

    /// <summary>Live door (TL2): the shared core's events, streamed as they happen.</summary>
    public IAsyncEnumerable<AgentStreamEvent> StreamAsync(LlmRequest req, int? maxIterations = null, CancellationToken ct = default)
        => RunCoreAsync(req, maxIterations, [], new UsageSum(), ct);

    /// <summary>The single event-producing core both doors share. Drives the loop (native / prompt / no-tools),
    /// yields live <see cref="AgentStreamEvent"/>s, and populates <paramref name="steps"/> + <paramref name="usage"/>
    /// as side outputs so <see cref="RunAsync"/> can fold the same run. Always ends with exactly one terminal
    /// <see cref="SessionEnded"/> (preceded by a <see cref="UsageFinal"/> when any provider reported usage).</summary>
    private async IAsyncEnumerable<AgentStreamEvent> RunCoreAsync(
        LlmRequest req, int? maxIterations, List<ToolStep> steps, UsageSum usage,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var activity = LyntaiDiagnostics.StartToolLoop(req.Consumer);
        var tools = registry.Tools;

        // Re-assert the loop span as current before each child-span-creating await. Activity.Current is an
        // AsyncLocal, and an async iterator resets it across every `yield return` (each MoveNextAsync runs in
        // the consumer's execution context) — so without this the per-turn chat spans and per-tool spans would
        // lose their parent. Only when there IS a loop activity (a Lyntai.Agents listener is attached); when
        // null we leave the consumer's ambient context untouched.
        void Enter() { if (activity is not null) Activity.Current = activity; }

        // The single terminal: a UsageFinal (when any usage was reported) then the SessionEnded. A local
        // iterator so every exit site emits it identically (re-yielded via `foreach … yield return`).
        IEnumerable<AgentStreamEvent> Finish(string mode, LlmVerdict verdict, string? finalText, string? detail)
        {
            if (usage.Value is { } u)
                yield return new UsageFinal(u.InputTokens, u.OutputTokens, u.CacheReadTokens, 0, null);
            LyntaiDiagnostics.EndToolLoop(activity, mode, steps.Count, verdict);
            yield return new SessionEnded(verdict, verdict != LlmVerdict.Ok, null, null, finalText, detail);
        }

        // No tools registered → a single plain completion (forcing the JSON protocol would be pointless
        // overhead and could mangle a direct answer).
        if (tools.Count == 0)
        {
            Enter();
            var direct = await client.CompleteAsync(req, ct).ConfigureAwait(false);
            usage.Add(direct.Usage);
            var ok = direct.Verdict == LlmVerdict.Ok;
            if (ok && direct.Text.Length > 0) yield return new TextDelta(direct.Text);
            foreach (var ev in Finish("none", direct.Verdict, ok ? direct.Text : "", direct.Detail)) yield return ev;
            yield break;
        }

        var budget = maxIterations ?? options.ToolLoopMaxIterations;

        if (client.SupportsToolCalls(req))
        {
            // Native path: tool declarations go to the model; its structured LlmReply.ToolCalls drive
            // execution, fed back as tool-role messages.
            var declarations = tools.Select(t => new LlmTool(t.Name, t.Description, t.ParametersJsonSchema)).ToList();
            var messages = new List<LlmMessage>(req.Messages); // no protocol prompt — the model calls tools natively

            for (var iteration = 0; iteration < budget; iteration++)
            {
                // CompleteAsync, NOT CompleteJsonAsync/StreamAsync: a native tool-call turn has empty text and
                // its structured ToolCalls (the loop's signal) aren't surfaced by the JSON/streaming contracts.
                Enter();
                var reply = await client.CompleteAsync(req with { Messages = [.. messages], Tools = declarations }, ct)
                    .ConfigureAwait(false);
                usage.Add(reply.Usage);
                if (reply.Verdict != LlmVerdict.Ok)
                {
                    foreach (var ev in Finish("native", reply.Verdict, null, reply.Detail)) yield return ev;
                    yield break;
                }
                if (reply.ToolCalls is not { Count: > 0 })
                {
                    _logger.LogDebug("tool-loop (native): final answer after {Steps} tool step(s)", steps.Count);
                    if (reply.Text.Length > 0) yield return new TextDelta(reply.Text);
                    foreach (var ev in Finish("native", LlmVerdict.Ok, reply.Text, null)) yield return ev; // no calls → answered
                    yield break;
                }

                // any prose the model emitted alongside the calls is surfaced (and preserved in the transcript);
                // then one tool-result per call (a missing tool_call_id makes providers reject the next request)
                if (!string.IsNullOrEmpty(reply.Text)) yield return new TextDelta(reply.Text);
                messages.Add(LlmMessage.AssistantToolCalls(reply.ToolCalls, reply.Text));
                foreach (var call in reply.ToolCalls)
                {
                    yield return new ToolCall(call.Name, call.ArgumentsJson, call.Id);
                    Enter();
                    var gated = await GatedInvokeAsync(call.Name, call.ArgumentsJson, ct).ConfigureAwait(false);
                    if (gated.Blocked)
                    {
                        yield return new ToolResult(call.Id, gated.Reason ?? "", true);
                        foreach (var ev in Finish("native", LlmVerdict.Refused, null, gated.Reason)) yield return ev;
                        yield break;
                    }
                    steps.Add(new ToolStep(call.Name, gated.Args, gated.Observation));
                    yield return new ToolResult(call.Id, gated.Observation, IsErrorObservation(gated.Observation));
                    messages.Add(LlmMessage.ToolResult(call.Id, gated.Observation));
                }
            }

            _logger.LogWarning("tool-loop (native): no final answer within {Budget} iterations", budget);
            foreach (var ev in Finish("native", LlmVerdict.Failed, null, $"tool loop did not converge within {budget} iterations")) yield return ev;
            yield break;
        }
        else
        {
            // Prompt-protocol fallback for providers without native tool-calling: {"tool":…}/{"final":…} over
            // the text contract via LlmStructuredExtensions.CompleteJsonAsync.
            var messages = new List<LlmMessage> { LlmMessage.System(BuildSystemPrompt(tools)) };
            messages.AddRange(req.Messages);

            for (var iteration = 0; iteration < budget; iteration++)
            {
                // Tools = null: this path drives tool use over TEXT; a caller-supplied req.Tools must not also
                // be sent as native declarations (tools come from the registry), or a partially-tool-aware
                // model emits a native tool_calls turn this path never parses.
                Enter();
                var reply = await client.CompleteJsonAsync(req with { Messages = [.. messages], Tools = null }, ct).ConfigureAwait(false);
                usage.Add(reply.Usage);
                if (reply.Verdict != LlmVerdict.Ok)
                {
                    foreach (var ev in Finish("prompt", reply.Verdict, null, reply.Detail)) yield return ev; // surface refusal / all-down
                    yield break;
                }
                if (!TryParseTurn(reply.Text, out var call))
                {
                    yield return new TextDelta(reply.Text); // no recognized key → a direct answer
                    foreach (var ev in Finish("prompt", LlmVerdict.Ok, reply.Text, null)) yield return ev;
                    yield break;
                }
                if (call.IsFinal)
                {
                    _logger.LogDebug("tool-loop: final answer after {Steps} tool step(s)", steps.Count);
                    yield return new TextDelta(call.FinalAnswer);
                    foreach (var ev in Finish("prompt", LlmVerdict.Ok, call.FinalAnswer, null)) yield return ev;
                    yield break;
                }

                yield return new ToolCall(call.ToolName, call.ArgumentsJson, null); // prompt protocol has no call id
                Enter();
                var gated = await GatedInvokeAsync(call.ToolName, call.ArgumentsJson, ct).ConfigureAwait(false);
                if (gated.Blocked)
                {
                    yield return new ToolResult(null, gated.Reason ?? "", true);
                    foreach (var ev in Finish("prompt", LlmVerdict.Refused, null, gated.Reason)) yield return ev;
                    yield break;
                }
                steps.Add(new ToolStep(call.ToolName, gated.Args, gated.Observation));
                yield return new ToolResult(null, gated.Observation, IsErrorObservation(gated.Observation));

                // feed the model its own tool-call turn, then the observation, and continue
                messages.Add(LlmMessage.Assistant(reply.Text));
                messages.Add(LlmMessage.User($"Tool \"{call.ToolName}\" returned:\n{gated.Observation}"));
            }

            _logger.LogWarning("tool-loop: no final answer within {Budget} iterations", budget);
            foreach (var ev in Finish("prompt", LlmVerdict.Failed, null, $"tool loop did not converge within {budget} iterations")) yield return ev;
            yield break;
        }
    }

    // A tool observation carrying an unknown-tool / threw-exception marker (see InvokeAsync) is flagged as an
    // error on the streamed ToolResult; the model still receives it and can recover.
    private static bool IsErrorObservation(string observation) => observation.StartsWith("error:", StringComparison.Ordinal);

    /// <summary>Gate a tool call's ARGS before it runs and its OBSERVATION after — the tool-loop guard hook
    /// (guards otherwise only cover the chat boundary, not model-driven tool calls). A Block in either
    /// direction aborts the loop as a jail violation; a Replace rewrites the args / redacts the observation.
    /// No guard rail (or no guards registered) → straight through.</summary>
    private async Task<Gated> GatedInvokeAsync(string name, string argumentsJson, CancellationToken ct)
    {
        if (guards is not null)
        {
            var pre = await guards.InspectToolCallAsync(name, argumentsJson, ct).ConfigureAwait(false);
            if (pre.Result == GuardOutcome.Kind.Block)
            {
                _logger.LogInformation("tool-loop: guard blocked tool call {Tool}: {Reason}", name, pre.Reason);
                return Gated.Block(pre.Reason ?? $"tool call '{name}' blocked by guard");
            }
            if (pre.Result == GuardOutcome.Kind.Replace) argumentsJson = pre.Replacement!;
        }

        var observation = await InvokeAsync(name, argumentsJson, ct).ConfigureAwait(false);

        if (guards is not null)
        {
            var post = await guards.InspectToolResultAsync(name, observation, ct).ConfigureAwait(false);
            if (post.Result == GuardOutcome.Kind.Block)
            {
                _logger.LogInformation("tool-loop: guard blocked the observation from {Tool}: {Reason}", name, post.Reason);
                return Gated.Block(post.Reason ?? $"observation from '{name}' blocked by guard");
            }
            if (post.Result == GuardOutcome.Kind.Replace) observation = post.Replacement!;
        }
        return Gated.Ok(observation, argumentsJson);
    }

    private readonly record struct Gated(bool Blocked, string? Reason, string Observation, string Args)
    {
        public static Gated Block(string reason) => new(true, reason, "", "");
        public static Gated Ok(string observation, string args) => new(false, null, observation, args);
    }

    /// <summary>Folds each front-door reply's <see cref="LlmUsage"/> into a running total (TL1). Stays null
    /// until at least one reply reports usage, so a run over providers that surface no tokens yields a null
    /// <see cref="ToolLoopResult.Usage"/> rather than a misleading all-zero figure.</summary>
    private sealed class UsageSum
    {
        public LlmUsage? Value { get; private set; }

        public void Add(LlmUsage? next)
        {
            if (next is null) return;
            if (Value is null) { Value = next; return; }
            var a = Value;
            // CostUsd sums only when at least one side reported one; both-null stays null (not 0).
            var cost = a.CostUsd is null && next.CostUsd is null ? (double?)null : (a.CostUsd ?? 0) + (next.CostUsd ?? 0);
            Value = new LlmUsage(
                a.InputTokens + next.InputTokens,
                a.OutputTokens + next.OutputTokens,
                a.CacheReadTokens + next.CacheReadTokens,
                cost);
        }
    }

    private async Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken ct)
    {
        var tool = registry.Find(name);
        if (tool is null)
        {
            var available = string.Join(", ", registry.Tools.Select(t => t.Name));
            return $"error: unknown tool \"{name}\". Available tools: {available}";
        }
        using var activity = LyntaiDiagnostics.StartToolCall(name);
        var error = false;
        try
        {
            _logger.LogDebug("tool-loop: invoking {Tool} with {Args}", name, argumentsJson);
            return await tool.InvokeAsync(argumentsJson, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // caller-initiated cancel is not a tool error
        }
        catch (Exception ex)
        {
            // a throwing tool is recoverable: report it back so the model can adjust, don't kill the loop
            error = true;
            _logger.LogWarning(ex, "tool-loop: tool {Tool} threw", name);
            return $"error: {ex.Message}";
        }
        finally
        {
            LyntaiDiagnostics.EndToolCall(activity, name, error);
        }
    }

    private static string BuildSystemPrompt(IReadOnlyList<ITool> tools)
    {
        var sb = new StringBuilder();
        sb.Append("You can use tools to answer the request. On each turn reply with EXACTLY ONE JSON object ");
        sb.Append("and nothing else, in one of these two forms:\n");
        sb.Append("  to call a tool:      {\"tool\": \"<name>\", \"arguments\": { ... }}\n");
        sb.Append("  for the final answer: {\"final\": \"<answer>\"}\n");
        sb.Append("After a tool call you receive its result, then continue. Only call tools listed below.\n\n");
        sb.Append("Tools:\n");
        foreach (var t in tools)
        {
            sb.Append("- ").Append(t.Name);
            if (!string.IsNullOrWhiteSpace(t.Description)) sb.Append(": ").Append(t.Description);
            sb.Append('\n');
            if (!string.IsNullOrWhiteSpace(t.ParametersJsonSchema))
                sb.Append("  arguments JSON schema: ").Append(t.ParametersJsonSchema).Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>Classify one protocol turn. A "final" key (any JSON value) ends the loop; a string
    /// "tool" key is a call with its "arguments" object (empty when absent). Anything else → not a turn
    /// (the caller treats it as a direct answer).</summary>
    internal static bool TryParseTurn(string json, out Turn turn)
    {
        turn = default;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return false;

            if (root.TryGetProperty("final", out var final))
            {
                var answer = final.ValueKind == JsonValueKind.String ? final.GetString() ?? "" : final.GetRawText();
                turn = new Turn(true, "", "", answer);
                return true;
            }
            if (root.TryGetProperty("tool", out var tool) && tool.ValueKind == JsonValueKind.String)
            {
                var name = tool.GetString() ?? "";
                if (name.Length == 0) return false;
                var args = root.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.Object
                    ? a.GetRawText()
                    : "{}";
                turn = new Turn(false, name, args, "");
                return true;
            }
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    internal readonly record struct Turn(bool IsFinal, string ToolName, string ArgumentsJson, string FinalAnswer);
}
