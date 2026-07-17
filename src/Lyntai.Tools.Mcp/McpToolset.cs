using System.Text.Json;
using System.Text.Json.Nodes;
using Lyntai.Agents;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Lyntai.Tools.Mcp;

/// <summary>
/// Adapts the tools exposed by a connected MCP server into Lyntai <see cref="ITool"/>s. The app owns
/// the <see cref="McpClient"/> (transport, connection, lifecycle — BYO); this just lists the server's
/// tools and wraps each so the <see cref="Lyntai.Agents.IToolLoop"/> can call it.
/// </summary>
public static class McpToolset
{
    /// <summary>List the server's tools and wrap each as an <see cref="ITool"/>. Call once at startup
    /// (after connecting the client); register the result with <c>builder.AddMcpTools(...)</c>.</summary>
    public static async Task<IReadOnlyList<ITool>> FromClientAsync(McpClient client, CancellationToken ct = default)
    {
        var mcpTools = await client.ListToolsAsync(cancellationToken: ct).ConfigureAwait(false);
        return [.. mcpTools.Select(FromMcpTool)];
    }

    internal static ITool FromMcpTool(McpClientTool tool) =>
        new McpTool(tool.Name, tool.Description, tool.JsonSchema.GetRawText(),
            (argsJson, ct) => CallAsync(tool, argsJson, ct));

    private static async Task<string> CallAsync(McpClientTool tool, string argsJson, CancellationToken ct)
    {
        var result = await tool.CallAsync(ParseArgs(argsJson), cancellationToken: ct).ConfigureAwait(false);
        return ToText(result);
    }

    /// <summary>Flatten a tool result to the observation string the loop feeds back: joined text blocks,
    /// or the structured content as JSON; prefixed <c>error:</c> when the server flagged an error.</summary>
    internal static string ToText(CallToolResult result)
    {
        var text = string.Join("\n", result.Content.OfType<TextContentBlock>().Select(b => b.Text));
        if (text.Length == 0 && result.StructuredContent is { } structured)
            text = structured.GetRawText();
        return result.IsError == true ? $"error: {text}" : text;
    }

    /// <summary>JSON arguments string → the dictionary the MCP call wants (values kept as detached
    /// <see cref="JsonNode"/>s — the SDK serializes them on the wire).</summary>
    private static IReadOnlyDictionary<string, object?> ParseArgs(string argsJson)
    {
        if (string.IsNullOrWhiteSpace(argsJson)) return EmptyArgs;
        try
        {
            return JsonNode.Parse(argsJson) is JsonObject obj
                ? obj.ToDictionary(kv => kv.Key, kv => (object?)kv.Value?.DeepClone())
                : EmptyArgs;
        }
        catch (JsonException) { return EmptyArgs; }
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyArgs = new Dictionary<string, object?>();
}
