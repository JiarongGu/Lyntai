using System.Text.Json;
using Lyntai.Agents;
using Lyntai.Guards;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Tools.Mcp.Hosting;

/// <summary>
/// An invocable <see cref="AIFunction"/> over a Lyntai <see cref="ITool"/> — the bridge that lets an
/// <see cref="ITool"/> be exposed as an MCP server tool (<c>McpServerTool.Create(this)</c>). Its
/// <see cref="JsonSchema"/> is the tool's own schema (not inferred from a delegate), and
/// <see cref="InvokeCoreAsync"/> runs the call through <see cref="ToolInvocation"/>, the flow the tool loop
/// shares: guarded both ways, a throw turned into an error observation, the span and counter recorded.
///
/// <para><b>A guard <c>Block</c> is ADVISORY here and TERMINAL in the tool loop.</b> This is one function
/// invocation inside a loop the CLIENT owns, and MCP has no "abandon the session" response, so a refusal can
/// only be reported: the tool does not run, its payload is never produced, and the model may retry within
/// the client's own loop budget. The built-in rail counts every block on <c>lyntai.guard.decisions</c>, so
/// repetition is observable (<c>docs/DECISIONS.md</c> D75).</para></summary>
internal sealed class ToolFunction(ITool tool, IGuardRail? guards = null, ILogger? logger = null) : AIFunction
{
    private readonly ILogger _logger = logger ?? NullLogger.Instance;

    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    private readonly JsonElement _schema = ParseSchema(tool.ParametersJsonSchema);

    public override string Name => tool.Name;
    public override string Description => tool.Description ?? "";
    public override JsonElement JsonSchema => _schema;

    /// <summary>Run the tool gated both ways. A block comes back as the refusal observation
    /// <see cref="GatedToolResult.Observation"/> carries, which is the enforcement available from this
    /// side.</summary>
    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var gated = await ToolInvocation.InvokeGatedAsync(
            tool, JsonArgs.Serialize(arguments), guards, _logger, cancellationToken).ConfigureAwait(false);
        return gated.Observation;
    }

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
