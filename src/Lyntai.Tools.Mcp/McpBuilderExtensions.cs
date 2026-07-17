using Lyntai.Agents;
using Microsoft.Extensions.DependencyInjection;

// Lives in the Lyntai namespace so `AddMcpTools` shows up right on the builder.
namespace Lyntai;

public static class McpBuilderExtensions
{
    /// <summary>Register MCP-server tools (from <see cref="Tools.Mcp.McpToolset.FromClientAsync"/>) into
    /// the tool-loop's tool collection. The app connects the MCP client in its own async startup and
    /// owns its lifecycle — Lyntai only adapts the tools:
    /// <code>
    /// await using var mcp = await McpClient.CreateAsync(transport);
    /// var tools = await McpToolset.FromClientAsync(mcp);
    /// services.AddLyntai(b => b.AddClaudeCliProvider().AddMcpTools(tools).DefaultCandidates("claude-cli"));
    /// </code></summary>
    public static LyntaiBuilder AddMcpTools(this LyntaiBuilder builder, IEnumerable<ITool> mcpTools)
    {
        foreach (var tool in mcpTools)
            builder.Services.AddSingleton(tool);
        return builder;
    }
}
