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
}
