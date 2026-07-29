using Lyntai.Agents;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddClaudeCliProvider` shows up right on the builder.
namespace Lyntai;

public static class ClaudeCliBuilderExtensions
{
    /// <summary>Register the `claude` CLI provider (id "claude-cli"). The spawned command honors
    /// <c>LYNTAI_PROVIDER_CMD</c> / <c>CLAUDE_CMD</c> env overrides (tests/e2e point these at the
    /// deterministic provider stub). If an <see cref="ICliToolProvisioner"/> is registered — via
    /// <c>AddMcpToolHost(new ClaudeCliMcpDialect())</c> from <c>Lyntai.Tools.Mcp.Hosting</c> — the CLI is
    /// given the app's registered tools over MCP; otherwise it runs tool-free as before.</summary>
    public static LyntaiBuilder AddClaudeCliProvider(this LyntaiBuilder builder)
    {
        builder.AddProvider(sp => new ClaudeCliProvider(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<ClaudeCliProvider>>(),
            provisioner: ResolveProvisioner(sp)));
        return builder;
    }

    /// <summary>Prefer the provisioner registered for THIS provider's id — several CLI providers can each
    /// host tools with their own dialect, and an unkeyed lookup would hand us whichever was registered
    /// first. The unkeyed service stays as the fallback so a hand-rolled
    /// <see cref="ICliToolProvisioner"/> registration (no dialect, no key) keeps working.</summary>
    private static ICliToolProvisioner? ResolveProvisioner(IServiceProvider sp) =>
        sp.GetKeyedService<ICliToolProvisioner>(ClaudeCliProvider.ProviderId)
        ?? sp.GetService<ICliToolProvisioner>();

    /// <summary>Register <see cref="ClaudeAgentSession"/> as the <see cref="IAgentSession"/> singleton.
    /// The spawned command honors <c>LYNTAI_PROVIDER_CMD</c> / <c>CLAUDE_CMD</c> env overrides so
    /// tests and e2e can point at a deterministic stub. The session uses the caller's
    /// <see cref="AgentSessionOptions.WorkingDirectory"/> (not the neutral temp dir used by the provider)
    /// because the agent is expected to operate inside the caller's project.</summary>
    public static LyntaiBuilder AddClaudeCliAgentSession(this LyntaiBuilder builder)
    {
        builder.Services.AddSingleton<IAgentSession>(sp => new ClaudeAgentSession(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<ClaudeAgentSession>>()));
        return builder;
    }
}
