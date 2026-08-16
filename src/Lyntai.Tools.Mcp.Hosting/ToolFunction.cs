using System.Text.Json;
using Lyntai.Agents;
using Microsoft.Extensions.AI;

namespace Lyntai.Tools.Mcp.Hosting;

/// <summary>
/// An invocable <see cref="AIFunction"/> over a Lyntai <see cref="ITool"/> — the bridge that lets an
/// <c>ITool</c> be exposed as an MCP server tool (<c>McpServerTool.Create(this)</c>). Its
/// <see cref="JsonSchema"/> is the tool's own schema (not inferred from a delegate), and
/// <see cref="InvokeCoreAsync"/> serializes the model-supplied arguments to JSON and calls the tool.
/// </summary>
internal sealed class ToolFunction(ITool tool, Lyntai.Guards.IGuardRail? guards = null) : AIFunction
{
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
                return $"error: tool call blocked by guard: {pre.Reason ?? "no reason given"}";
            if (pre.Result == Lyntai.Guards.GuardOutcome.Kind.Replace) argsJson = pre.Replacement!;
        }

        var observation = await tool.InvokeAsync(argsJson, cancellationToken).ConfigureAwait(false);

        if (guards is not null)
        {
            var post = await guards.InspectToolResultAsync(tool.Name, observation, cancellationToken).ConfigureAwait(false);
            if (post.Result == Lyntai.Guards.GuardOutcome.Kind.Block)
                return $"error: observation blocked by guard: {post.Reason ?? "no reason given"}";
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
