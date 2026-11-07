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
        // an argument string that is not a JSON object is the model's mistake, said back to it: calling the tool
        // with its defaults instead would run something the model never asked for
        if (!TryParseArgs(argsJson, out var args))
            return ToolObservations.Error("the arguments are not a JSON object; send one, or {} for a tool that takes none");
        var result = await tool.CallAsync(args, cancellationToken: ct).ConfigureAwait(false);
        return ToText(result);
    }

    /// <summary>Flatten a tool result to the observation string the loop feeds back: the text of every text block
    /// and embedded text resource and the address of every resource link, in order, or the structured content as
    /// JSON; prefixed <c>error:</c> when the server flagged an error. A blob or an image is described, never read.</summary>
    internal static string ToText(CallToolResult result)
    {
        var text = string.Join("\n", result.Content.Select(TextOf).OfType<string>());
        if (text.Length == 0 && result.StructuredContent is { } structured)
            text = structured.GetRawText();
        // no text and no structured content, but non-text blocks (image/audio/resource) — don't feed an
        // EMPTY observation back (the model would think the tool produced nothing); describe the blocks
        if (text.Length == 0 && result.Content.Count > 0)
            text = string.Join("\n", result.Content.Select(b => $"[{b.Type} content]"));
        if (result.IsError == true)
            return ToolObservations.Error(text.Length > 0 ? text : "tool reported an error with no message");
        return text;
    }

    private static string? TextOf(ContentBlock block) => block switch
    {
        TextContentBlock t => t.Text,
        EmbeddedResourceBlock { Resource: TextResourceContents r } => r.Text,
        ResourceLinkBlock l => $"[resource link: {l.Name} {l.Uri}]",
        _ => null,
    };

    /// <summary>JSON arguments string → the dictionary the MCP call wants (values kept as detached
    /// <see cref="JsonNode"/>s — the SDK serializes them on the wire), through the same
    /// <see cref="JsonArgs"/> the tool host serializes with. A blank string, <c>null</c> and <c>{}</c> are
    /// no arguments; anything else that is not a JSON object is refused.</summary>
    private static bool TryParseArgs(string? argsJson, out IReadOnlyDictionary<string, object?> args)
    {
        args = EmptyArgs;
        if (string.IsNullOrWhiteSpace(argsJson) || argsJson.Trim() == "null") return true;
        if (JsonArgs.Parse(argsJson) is not IReadOnlyDictionary<string, object?> parsed) return false;
        args = parsed;
        return true;
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyArgs = new Dictionary<string, object?>();
}
