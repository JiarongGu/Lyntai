using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// string, reflection-free via <see cref="JsonNode"/>.</summary>
    private static string SerializeArgs(IEnumerable<KeyValuePair<string, object?>> arguments)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in arguments)
            obj[key] = ToNode(value);
        return obj.ToJsonString();
    }

    /// <summary>An argument value → a JSON node, preserving type. Values arrive as <see cref="JsonElement"/>
    /// from the wire but can be boxed CLR primitives; those must keep their JSON type (a <c>3</c> must not
    /// become <c>"3"</c>). Reflection-free (typed <c>JsonValue.Create</c> overloads etc.).</summary>
    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode n => n.DeepClone(),
        JsonElement e => JsonNode.Parse(e.GetRawText()),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        long l => JsonValue.Create(l),
        double d => JsonValue.Create(d),
        decimal m => JsonValue.Create(m),
        string s => JsonValue.Create(s),
        var other => JsonValue.Create(other.ToString()),
    };

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
