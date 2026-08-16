using Lyntai.Agents;
using Lyntai.Tools.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Lives in the Lyntai namespace so `AddMcpToolHost` shows up right on the builder.
namespace Lyntai;

public static class McpToolHostBuilderExtensions
{
    /// <summary>Give a CLI provider that runs its OWN agent loop proper tool-calling: the app's registered
    /// <see cref="ITool"/>s become available to that CLI's agent over an in-process, localhost-only HTTP
    /// MCP server which Lyntai stands up per CLI call (and tears down after). Opt-in — call this alongside
    /// the provider registration and your <c>AddTool(...)</c>/<c>AddMcpTools(...)</c> registrations:
    /// <code>
    /// services.AddLyntai(b => b
    ///     .AddClaudeCliProvider()
    ///     .AddMcpToolHost(new ClaudeCliMcpDialect())
    ///     .AddTool(_ => new FunctionTool("get_status", …)));
    /// </code>
    /// <para>The provisioner is registered KEYED on <see cref="IMcpCliDialect.ProviderId"/>, so several CLI
    /// providers can host tools side by side with different dialects; the FIRST registration additionally
    /// becomes the unkeyed fallback for any provider that resolves without a key.</para>
    /// <para>Note: this runs an ephemeral <c>HttpListener</c> (BCL — no ASP.NET Core, no framework
    /// reference) on loopback during each CLI completion — a deliberate, scoped exception to the library's
    /// otherwise host-free design.</para></summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="dialect">Supplies the CLI's flags and config-file shapes — e.g.
    /// <c>ClaudeCliMcpDialect</c> from <c>Lyntai.Providers.ClaudeCli</c>.</param>
    /// <param name="configure">Optional host tweaks (MCP server name, bind address).</param>
    public static LyntaiBuilder AddMcpToolHost(
        this LyntaiBuilder builder, IMcpCliDialect dialect, Action<McpToolHostOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        var options = new McpToolHostOptions();
        configure?.Invoke(options);

        // The guard rail is resolved OPTIONALLY and travels into the host: this endpoint executes the same
        // ITool instances the in-process tool loop does, so a rail applied to one and not the other is not
        // applied. Resolved rather than required so an app with no guards wires exactly as before.
        builder.Services.AddKeyedSingleton<ICliToolProvisioner>(dialect.ProviderId,
            (sp, _) => new McpToolHostProvisioner(sp.GetServices<ITool>(), dialect, options,
                sp.GetService<Lyntai.Guards.IGuardRail>()));

        // the first dialect registered also answers the unkeyed lookup, so a provider that doesn't ask by
        // key (and the single-CLI case, which is most apps) keeps working with no extra wiring
        builder.Services.TryAddSingleton<ICliToolProvisioner>(sp =>
            sp.GetRequiredKeyedService<ICliToolProvisioner>(dialect.ProviderId));

        return builder;
    }
}
