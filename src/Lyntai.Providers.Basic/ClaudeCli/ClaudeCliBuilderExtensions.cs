using Lyntai.Agents;
using Lyntai.Processes;
using Lyntai.Providers.Basic;
using Lyntai.Providers.ClaudeCli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddClaudeCliProvider` shows up right on the builder.
namespace Lyntai;

/// <summary>DI entry points for the <c>claude</c> CLI: the completion provider and the self-driving agent
/// session. A consumer composes them through the builder and never constructs their types by hand.</summary>
public static class ClaudeCliBuilderExtensions
{
    /// <summary>Register the `claude` CLI provider (id "claude-cli" unless <paramref name="id"/> names
    /// another). With no arguments the spawned command honors <c>LYNTAI_PROVIDER_CMD</c> / <c>CLAUDE_CMD</c>
    /// env overrides (tests/e2e point these at the deterministic provider stub), then falls back to
    /// <c>claude</c> on PATH. If an <see cref="ICliToolProvisioner"/> is registered — via
    /// <c>AddMcpToolHost(new ClaudeCliMcpConnector())</c> from <c>Lyntai.Tools.Mcp</c> — the CLI is given the
    /// app's registered tools over MCP; otherwise it runs tool-free.</summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="command">A PORTABLE install: the path to a <c>claude</c> the app ships or unpacks itself
    /// instead of a global one (quote a path with spaces). Read this from your own configuration and pass it
    /// here — no process-wide environment variable needed.</param>
    /// <param name="environment">Extra environment variables for every spawn; a portable install usually
    /// wants its own <c>CLAUDE_CONFIG_DIR</c> so it neither reads nor mutates the machine-wide install's state.</param>
    /// <param name="id">The router-facing id. Two registrations — two portable installs, two accounts — need
    /// distinct ids, or the first-wins router never reaches the second. The tool provisioner keyed on this id
    /// is preferred, then the one keyed on "claude-cli", then an unkeyed one.</param>
    public static LyntaiBuilder AddClaudeCliProvider(
        this LyntaiBuilder builder,
        string? command = null,
        IReadOnlyDictionary<string, string>? environment = null,
        string id = ClaudeCliProvider.ProviderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        builder.AddProvider(sp => new ClaudeCliProvider(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<ClaudeCliProvider>>(),
            command,
            CliComposition.Provisioner(sp, id, ClaudeCliProvider.ProviderId),
            environment,
            id));
        return builder;
    }

    /// <summary>Register <see cref="ClaudeAgentSession"/> as an <see cref="IAgentSession"/>.
    /// The spawned command honors <c>LYNTAI_PROVIDER_CMD</c> / <c>CLAUDE_CMD</c> env overrides so
    /// tests and e2e can point at a deterministic stub. The session uses the caller's
    /// <see cref="AgentSessionOptions.WorkingDirectory"/> (not the neutral temp dir used by the provider)
    /// because the agent is expected to operate inside the caller's project.
    /// <para>Registered both unkeyed and KEYED by <paramref name="id"/>, so an app that also registers
    /// <c>AddCodexCliAgentSession</c> resolves the one it means
    /// (<c>GetRequiredKeyedService&lt;IAgentSession&gt;("claude-cli")</c>) instead of whichever registration
    /// happened to be last.</para></summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="command">A PORTABLE <c>claude</c> path, as with
    /// <see cref="AddClaudeCliProvider"/> — pass the same value to both so a host's bundled CLI is used for
    /// completions and agent sessions alike.</param>
    /// <param name="environment">Extra environment variables for every spawn — again, pass the SAME value
    /// here as to <see cref="AddClaudeCliProvider"/>: a portable install usually wants its own
    /// <c>CLAUDE_CONFIG_DIR</c> so it neither reads nor mutates the machine-wide install's state.</param>
    /// <param name="id">The key the session is registered under; "claude-cli" by default.</param>
    public static LyntaiBuilder AddClaudeCliAgentSession(this LyntaiBuilder builder, string? command = null,
        IReadOnlyDictionary<string, string>? environment = null, string id = ClaudeCliProvider.ProviderId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        builder.Services.AddSingleton<IAgentSession>(sp => CreateSession(sp, command, environment));
        builder.Services.AddKeyedSingleton<IAgentSession>(id, (sp, _) => CreateSession(sp, command, environment));
        return builder;
    }

    private static ClaudeAgentSession CreateSession(IServiceProvider sp, string? command,
        IReadOnlyDictionary<string, string>? environment) =>
        new(sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<ClaudeAgentSession>>(),
            command,
            environment);
}
