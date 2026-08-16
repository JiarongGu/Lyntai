using System.Text.Json;
using Lyntai.Agents;
using Microsoft.Extensions.AI;

namespace Lyntai.Tools.Mcp.Hosting;

/// <summary>
/// An invocable <see cref="AIFunction"/> over a Lyntai <see cref="ITool"/> — the bridge that lets an
/// <see cref="ITool"/> be exposed as an MCP server tool (<c>McpServerTool.Create(this)</c>). Its
/// <see cref="JsonSchema"/> is the tool's own schema (not inferred from a delegate), and
/// <see cref="InvokeCoreAsync"/> serializes the model-supplied arguments to JSON and calls the tool.
///
/// <para><b>A guard <c>Block</c> is ADVISORY here and TERMINAL in the tool loop, and that difference is
/// deliberate — it follows from who owns the loop.</b> <c>ToolLoop</c> drives the turn, so it can end it
/// with <see cref="Lyntai.Llm.LlmVerdict.Refused"/>. This is one MCP function invocation inside a loop the
/// CLIENT owns, and MCP has no "abandon the session" response: a tool may return content or an error, and
/// nothing else. So a refusal here can only be reported, never enforced by termination. Making it "terminal"
/// would mean throwing, which the client is equally free to catch and retry — the appearance of force
/// without the substance.</para>
///
/// <para><b>What the model may therefore do, stated plainly:</b> retry a blocked call with different
/// arguments. The bound on that is the client's own loop budget, not this method. The library deliberately
/// does not invent a retry-counting policy here — that is state this seam does not have and a rule no
/// consumer has yet asked for — but repetition IS observable: every block increments the guard-decision
/// counter (<c>lyntai.guard.result=block</c>), so an operator can alert on a rate rather than discover it.
/// A concrete report of a model looping on a blocked tool is what would justify more than that; see
/// <c>docs/DECISIONS.md</c> D75.</para></summary>
internal sealed class ToolFunction(
    ITool tool,
    Lyntai.Guards.IGuardRail? guards = null,
    Microsoft.Extensions.Logging.ILogger? logger = null) : AIFunction
{
    private readonly Microsoft.Extensions.Logging.ILogger _logger =
        logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    private readonly JsonElement _schema = ParseSchema(tool.ParametersJsonSchema);

    public override string Name => tool.Name;
    public override string Description => tool.Description ?? "";
    public override JsonElement JsonSchema => _schema;

    /// <summary>Run the tool, gated both ways when a rail is present — args BEFORE it executes, observation
    /// BEFORE it goes back to the model. This is the same pair <c>ToolLoop.GatedInvokeAsync</c> applies, and
    /// it has to be here too because this endpoint is a SECOND door onto the same <see cref="ITool"/>
    /// instances: a guard enforced on one door and not the other is not enforced at all.
    /// <para>A Block cannot abort the CLI's own agent loop the way it aborts this library's, so it is
    /// reported to the model as a refusal observation — the tool does not run and its payload is never
    /// produced, which is the enforcement actually available from this side.</para></summary>
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var argsJson = SerializeArgs(arguments);

        if (guards is not null)
        {
            var pre = await guards.InspectToolCallAsync(tool.Name, argsJson, cancellationToken).ConfigureAwait(false);
            if (pre.Result == Lyntai.Guards.GuardOutcome.Kind.Block)
            {
                // LOGGED, matching what ToolLoop already does on its own guard path. Both doors always
                // emitted the OTel counter (IGuardRail records it), so a block was never invisible — but a
                // host reading LOGS saw a refusal from one door and silence from the other, purely because
                // one class had a logger and this one did not. See the class remarks for why the difference
                // in FORCE is deliberate while this difference in SIGNAL was an accident.
                Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(
                    _logger, "mcp-host: guard blocked tool call {Tool}: {Reason}", tool.Name, pre.Reason);
                return $"error: tool call blocked by guard: {pre.Reason ?? "no reason given"}";
            }
            if (pre.Result == Lyntai.Guards.GuardOutcome.Kind.Replace) argsJson = pre.Replacement!;
        }

        var observation = await tool.InvokeAsync(argsJson, cancellationToken).ConfigureAwait(false);

        if (guards is not null)
        {
            var post = await guards.InspectToolResultAsync(tool.Name, observation, cancellationToken).ConfigureAwait(false);
            if (post.Result == Lyntai.Guards.GuardOutcome.Kind.Block)
            {
                Microsoft.Extensions.Logging.LoggerExtensions.LogInformation(
                    _logger, "mcp-host: guard blocked the observation from {Tool}: {Reason}", tool.Name, post.Reason);
                return $"error: observation blocked by guard: {post.Reason ?? "no reason given"}";
            }
            if (post.Result == Lyntai.Guards.GuardOutcome.Kind.Replace) observation = post.Replacement!;
        }

        return observation;
    }

    /// <summary>Model-supplied arguments (values are typically <see cref="JsonElement"/>) → a JSON object
    /// string, reflection-free via the shared <see cref="Lyntai.Text.JsonArgs"/> (kept in sync with the
    /// MEAI provider bridge).</summary>
    private static string SerializeArgs(IEnumerable<KeyValuePair<string, object?>> arguments) =>
        Lyntai.Text.JsonArgs.Serialize(arguments);

    private static JsonElement ParseSchema(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return EmptyObjectSchema;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return EmptyObjectSchema;
        }
    }
}
