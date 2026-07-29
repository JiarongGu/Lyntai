using Lyntai.Agents;
using Lyntai.Providers.ClaudeCli;
using Lyntai.Tools.Mcp.Hosting;

// Lives in the Lyntai namespace so `AddClaudeCliMcpTools` shows up right on the builder.
namespace Lyntai;

public static class ClaudeCliMcpBuilderExtensions
{
    /// <summary>Give the claude CLI provider proper tool-calling: the app's registered
    /// <see cref="ITool"/>s become available to the CLI's agent over an in-process, localhost-only HTTP
    /// MCP server that Lyntai stands up per CLI call (and tears down after). Opt-in — call this alongside
    /// <c>AddClaudeCliProvider()</c> and your <c>AddTool(...)</c>/<c>AddMcpTools(...)</c> registrations.
    /// <para>Shorthand for <c>AddMcpToolHost(new ClaudeCliMcpDialect())</c> — the generic host plus the
    /// claude dialect. Use <c>AddMcpToolHost</c> directly to host tools for a different CLI, or to run
    /// several CLI dialects side by side.</para>
    /// <para>Note: this runs an ephemeral Kestrel listener on loopback during each CLI completion — a
    /// deliberate, scoped exception to the library's otherwise host-free design.</para></summary>
    public static LyntaiBuilder AddClaudeCliMcpTools(this LyntaiBuilder builder) =>
        builder.AddMcpToolHost(new ClaudeCliMcpDialect());

    /// <summary>As <see cref="AddClaudeCliMcpTools(LyntaiBuilder)"/>, with host tweaks (MCP server name,
    /// bind address).</summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="configure">Applied to the host options before the provisioner is registered.</param>
    public static LyntaiBuilder AddClaudeCliMcpTools(this LyntaiBuilder builder, Action<McpToolHostOptions> configure) =>
        builder.AddMcpToolHost(new ClaudeCliMcpDialect(), configure);
}
