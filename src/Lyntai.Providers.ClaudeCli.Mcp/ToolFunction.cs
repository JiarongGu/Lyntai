using System.Text.Json;
using Lyntai.Agents;
using Microsoft.Extensions.AI;

namespace Lyntai.Providers.ClaudeCli.Mcp;

/// <summary>
/// An invocable <see cref="AIFunction"/> over a Lyntai <see cref="ITool"/> — the bridge that lets an
/// <c>ITool</c> be exposed as an MCP server tool (<c>McpServerTool.Create(this)</c>). Its
/// <see cref="JsonSchema"/> is the tool's own schema (not inferred from a delegate), and
/// <see cref="InvokeCoreAsync"/> serializes the model-supplied arguments to JSON and calls the tool.
/// </summary>
internal sealed class ToolFunction(ITool tool) : AIFunction
{
    private static readonly JsonElement EmptyObjectSchema = JsonDocument.Parse("""{"type":"object"}""").RootElement.Clone();

    private readonly JsonElement _schema = ParseSchema(tool.ParametersJsonSchema);

    public override string Name => tool.Name;
    public override string Description => tool.Description ?? "";
    public override JsonElement JsonSchema => _schema;

    protected override async ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        var argsJson = SerializeArgs(arguments);
        return await tool.InvokeAsync(argsJson, cancellationToken).ConfigureAwait(false);
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
