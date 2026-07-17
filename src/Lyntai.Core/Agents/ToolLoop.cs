using System.Text;
using System.Text.Json;
using Lyntai.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Agents;

/// <summary>
/// Default <see cref="IToolLoop"/>. The JSON protocol each turn: the model replies with exactly one
/// object, either <c>{"tool":"&lt;name&gt;","arguments":{…}}</c> to call a tool or
/// <c>{"final":"&lt;answer&gt;"}</c> to finish. Replies go through <see cref="LlmStructuredExtensions.CompleteJsonAsync"/>,
/// so a parseable object is guaranteed (tolerant extraction + one corrective retry). Unknown tools and
/// tools that throw become <c>error: …</c> observations fed back to the model (it can recover) rather
/// than exceptions. A non-Ok LLM verdict (refusal, all candidates down) is surfaced as-is.
/// </summary>
public sealed class ToolLoop(
    ILlmClient client,
    IToolRegistry registry,
    LyntaiOptions options,
    ILogger<ToolLoop>? logger = null) : IToolLoop
{
    private readonly ILogger _logger = logger ?? NullLogger<ToolLoop>.Instance;

    public async Task<ToolLoopResult> RunAsync(LlmRequest req, int? maxIterations = null, CancellationToken ct = default)
    {
        var tools = registry.Tools;
        var steps = new List<ToolStep>();

        // No tools registered → the loop degenerates to a single plain completion (forcing the JSON
        // protocol would be pointless overhead and could mangle a direct answer).
        if (tools.Count == 0)
        {
            var direct = await client.CompleteAsync(req, ct).ConfigureAwait(false);
            return new ToolLoopResult(direct.Verdict == LlmVerdict.Ok ? direct.Text : "", direct.Verdict, steps, direct.Detail);
        }

        var budget = maxIterations ?? options.ToolLoopMaxIterations;
        var messages = new List<LlmMessage> { LlmMessage.System(BuildSystemPrompt(tools)) };
        messages.AddRange(req.Messages);

        for (var iteration = 0; iteration < budget; iteration++)
        {
            var reply = await client.CompleteJsonAsync(req with { Messages = [.. messages] }, ct).ConfigureAwait(false);
            if (reply.Verdict != LlmVerdict.Ok)
                return new ToolLoopResult("", reply.Verdict, steps, reply.Detail); // surface refusal / all-down as-is

            if (!TryParseTurn(reply.Text, out var call))
                return new ToolLoopResult(reply.Text, LlmVerdict.Ok, steps); // no recognized key → treat as a direct answer

            if (call.IsFinal)
            {
                _logger.LogDebug("tool-loop: final answer after {Steps} tool step(s)", steps.Count);
                return new ToolLoopResult(call.FinalAnswer, LlmVerdict.Ok, steps);
            }

            var observation = await InvokeAsync(call.ToolName, call.ArgumentsJson, ct).ConfigureAwait(false);
            steps.Add(new ToolStep(call.ToolName, call.ArgumentsJson, observation));

            // feed the model its own tool-call turn, then the observation, and continue
            messages.Add(LlmMessage.Assistant(reply.Text));
            messages.Add(LlmMessage.User($"Tool \"{call.ToolName}\" returned:\n{observation}"));
        }

        _logger.LogWarning("tool-loop: no final answer within {Budget} iterations", budget);
        return new ToolLoopResult("", LlmVerdict.Failed, steps, $"tool loop did not converge within {budget} iterations");
    }

    private async Task<string> InvokeAsync(string name, string argumentsJson, CancellationToken ct)
    {
        var tool = registry.Find(name);
        if (tool is null)
        {
            var available = string.Join(", ", registry.Tools.Select(t => t.Name));
            return $"error: unknown tool \"{name}\". Available tools: {available}";
        }
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
            _logger.LogWarning(ex, "tool-loop: tool {Tool} threw", name);
            return $"error: {ex.Message}";
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
