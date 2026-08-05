using Lyntai.Agents;
using Microsoft.Extensions.DependencyInjection;

// Lives in the Lyntai namespace so `AddMcpTools` shows up right on the builder.
namespace Lyntai;

/// <summary>Registers MCP-server tools onto the <see cref="LyntaiBuilder"/>. The INBOUND half of the MCP
/// story (a server's tools become Lyntai <see cref="ITool"/>s); the outbound half — exposing Lyntai's tools
/// to a CLI that speaks MCP — is <c>Lyntai.Tools.Mcp.Hosting</c>.</summary>
public static class McpBuilderExtensions
{
    /// <summary>Register MCP-server tools (from <see cref="Tools.Mcp.McpToolset.FromClientAsync"/>) into
    /// the tool-loop's tool collection.
    /// <para><b>The intended shape is two steps, and it is two steps on purpose.</b> Connecting an MCP
    /// client is asynchronous and its lifetime outlives the registration, while <c>AddLyntai</c> is a
    /// synchronous composition call — so the app connects the client in its own async startup, keeps
    /// ownership of it (transport, reconnection, disposal), and hands Lyntai only the adapted tools. Lyntai
    /// never opens, owns, or closes an MCP connection; it would have no idea when to.</para>
    /// <code>
    /// await using var mcp = await McpClient.CreateAsync(transport);   // 1. the app owns the client
    /// var tools = await McpToolset.FromClientAsync(mcp);              // 2. adapt its tools once
    /// services.AddLyntai(b => b.AddClaudeCliProvider().AddMcpTools(tools).UseDefaultCandidates("claude-cli"));
    /// </code>
    /// Call it once per connected server; several servers just call it several times (each tool is its own
    /// registration, so the sets merge rather than replace).</summary>
    public static LyntaiBuilder AddMcpTools(this LyntaiBuilder builder, IEnumerable<ITool> mcpTools)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(mcpTools);
        foreach (var tool in mcpTools)
            builder.Services.AddSingleton(tool);
        return builder;
    }

    /// <summary>Inline overload of <see cref="AddMcpTools(LyntaiBuilder, IEnumerable{ITool})"/> for a known
    /// handful of tools — a hand-picked subset of a server's toolset, or a BYO <see cref="ITool"/> alongside
    /// them — without wrapping them in a collection first. Identical behavior; the sequence overload remains
    /// the one <see cref="Tools.Mcp.McpToolset.FromClientAsync"/>'s result flows into.</summary>
    public static LyntaiBuilder AddMcpTools(this LyntaiBuilder builder, params ITool[] mcpTools) =>
        builder.AddMcpTools((IEnumerable<ITool>)mcpTools);
}
