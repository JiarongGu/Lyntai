using Lyntai.Agents;
using Lyntai.Providers.ClaudeCli.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so `AddClaudeCliMcpTools` shows up right on the builder.
namespace Lyntai;

public static class ClaudeCliMcpBuilderExtensions
{
    /// <summary>Give the claude CLI provider proper tool-calling: the app's registered
    /// <see cref="ITool"/>s become available to the CLI's agent over an in-process, localhost-only HTTP
    /// MCP server that Lyntai stands up per CLI call (and tears down after). Opt-in — call this alongside
    /// <c>AddClaudeCliProvider()</c> and your <c>AddTool(...)</c>/<c>AddMcpTools(...)</c> registrations.
    /// <para>Note: this runs an ephemeral Kestrel listener on <c>127.0.0.1</c> during each CLI
    /// completion — a deliberate, scoped exception to the library's otherwise host-free design.</para></summary>
    public static LyntaiBuilder AddClaudeCliMcpTools(this LyntaiBuilder builder)
    {
        builder.Services.TryAddSingleton<ICliToolProvisioner>(sp =>
            new McpCliToolProvisioner(sp.GetServices<ITool>()));
        return builder;
    }
}
