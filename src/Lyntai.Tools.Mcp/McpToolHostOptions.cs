namespace Lyntai.Tools.Mcp.Hosting;

/// <summary>Knobs for the ephemeral MCP tool host stood up per CLI invocation. The defaults are what you
/// want: a loopback listener on an OS-assigned port, published under the server name <c>lyntai</c>.</summary>
public sealed class McpToolHostOptions
{
    /// <summary>The default MCP server name the tools are published under.</summary>
    public const string DefaultServerName = "lyntai";

    /// <summary>The default listener address — loopback, OS-assigned port (<c>0</c>).</summary>
    public const string DefaultBindAddress = "http://127.0.0.1:0";

    /// <summary>The MCP server name the tools are published under. Some CLIs derive tool-permission
    /// patterns from it (e.g. <c>mcp__&lt;server&gt;__*</c>), so change it only if it collides with
    /// another MCP server the CLI already has configured.</summary>
    public string ServerName { get; set; } = DefaultServerName;

    /// <summary>The address the host binds. Keep it on loopback: the endpoint EXECUTES the app's tools.
    /// Port <c>0</c> lets the OS assign a free one, which is what makes the host safe to start per call.</summary>
    public string BindAddress { get; set; } = DefaultBindAddress;

    /// <summary>Which registered tools a spawn hosts, by the request's <see cref="Lyntai.Inference.TextRequest.Consumer"/>
    /// — the same per-consumer shape as <c>LyntaiOptions.TimeoutByConsumer</c>, keys ignoring case. A consumer
    /// absent from it gets every registered tool; an EMPTY list gets none, and no host is started, which is the
    /// fast plain call; names get only those tools, matched ordinally. Configuration rather than a request field,
    /// so a call that falls back to an HTTP backend never carries a request whose meaning changed. A name no
    /// registered tool has is refused when the provisioner is built.</summary>
    public Dictionary<string, IReadOnlyList<string>> ToolsByConsumer { get; } = new(StringComparer.OrdinalIgnoreCase);
}
