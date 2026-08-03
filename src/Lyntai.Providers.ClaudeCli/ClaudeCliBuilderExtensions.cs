using Lyntai.Agents;
using Lyntai.Processes;
using Lyntai.Providers.ClaudeCli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddClaudeCliProvider` shows up right on the builder.
namespace Lyntai;

public static class ClaudeCliBuilderExtensions
{
    /// <summary>Register the `claude` CLI provider (id "claude-cli"). With no arguments the spawned command
    /// honors <c>LYNTAI_PROVIDER_CMD</c> / <c>CLAUDE_CMD</c> env overrides (tests/e2e point these at the
    /// deterministic provider stub), then falls back to <c>claude</c> on PATH. If an
    /// <see cref="ICliToolProvisioner"/> is registered — via <c>AddMcpToolHost(new ClaudeCliMcpDialect())</c>
    /// from <c>Lyntai.Tools.Mcp.Hosting</c> — the CLI is given the app's registered tools over MCP;
    /// otherwise it runs tool-free.</summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="command">A PORTABLE install: the path to a <c>claude</c> the app ships or unpacks itself
    /// instead of a global one (quote a path with spaces). Read this from your own configuration and pass it
    /// here — no process-wide environment variable needed.</param>
    /// <param name="environment">Extra environment variables for every spawn; a portable install usually
    /// wants its own <c>CLAUDE_CONFIG_DIR</c> so it neither reads nor mutates the machine-wide install's state.</param>
    public static LyntaiBuilder AddClaudeCliProvider(
        this LyntaiBuilder builder,
        string? command = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        builder.AddProvider(sp => new ClaudeCliProvider(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<ClaudeCliProvider>>(),
            command,
            ResolveProvisioner(sp),
            environment));
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
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="command">A PORTABLE <c>claude</c> path, as with
    /// <see cref="AddClaudeCliProvider"/> — pass the same value to both so a host's bundled CLI is used for
    /// completions and agent sessions alike.</param>
    public static LyntaiBuilder AddClaudeCliAgentSession(this LyntaiBuilder builder, string? command = null)
    {
        builder.Services.AddSingleton<IAgentSession>(sp => new ClaudeAgentSession(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<ClaudeAgentSession>>(),
            command));
        return builder;
    }
}
