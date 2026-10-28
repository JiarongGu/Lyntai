using Lyntai.Agents;
using Lyntai.Processes;
using Lyntai.Providers.Acp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddAcpAgentSession` shows up right on the builder.
namespace Lyntai;

/// <summary>DI entry point for an agent that speaks the Agent Client Protocol. A consumer composes it through the
/// builder and never constructs its types by hand.</summary>
public static class AcpBuilderExtensions
{
    /// <summary>Register an agent reached over the Agent Client Protocol as an <see cref="IAgentSession"/> KEYED by
    /// <paramref name="id"/> and as an <see cref="IAgentProvider"/> under the same id (through
    /// <see cref="AgentProviderRegistry"/>), with one door, <c>"acp"</c>, of kind <see cref="AgentDoorKinds.Acp"/>.
    /// Unlike the two CLI registrations it adds no UNKEYED <see cref="IAgentSession"/>, so a host that resolves
    /// the unkeyed one keeps getting the CLI door it registered. Read <see cref="AcpAgentSession"/>'s remarks for
    /// what a turn does and for what its events and failures are read from.</summary>
    /// <remarks>An ACP agent and the CLI of the same vendor are two providers here, with two ids: an
    /// <see cref="AgentAccount"/> names the provider it belongs to, so one built for <c>"claude-cli"</c> is refused
    /// by <c>"claude-acp"</c>. Build one account per provider, over the same home.</remarks>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="id">The provider id and the key of the session, such as <c>"claude-acp"</c>.</param>
    /// <param name="command">The agent's launch command (<c>claude-agent-acp</c>, <c>codex-acp</c>, or a path to a
    /// portable copy; quote a path with spaces).</param>
    /// <param name="arguments">Arguments added after the command's own, each a separate entry.</param>
    /// <param name="environment">Extra environment variables for every spawn. A null value REMOVES a variable the
    /// host process holds (an inherited API key, say) from the child.</param>
    /// <param name="accountHomeVariable">The variable the agent reads its configuration home from —
    /// <c>CLAUDE_CONFIG_DIR</c> for <c>claude-agent-acp</c>, <c>CODEX_HOME</c> for <c>codex-acp</c>. Setting it
    /// makes the door state <see cref="AgentAbilities.Accounts"/> and read <see cref="AgentSessionOptions.Account"/>;
    /// null states it unsupported and refuses a turn that names an account. <c>codex-acp</c> exits at startup when
    /// the home it is given does not exist.</param>
    public static LyntaiBuilder AddAcpAgentSession(
        this LyntaiBuilder builder,
        string id,
        string command,
        IReadOnlyList<string>? arguments = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        string? accountHomeVariable = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        builder.Services.AddKeyedSingleton<IAgentSession>(id, (sp, _) => new AcpAgentSession(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            command,
            arguments,
            environment,
            accountHomeVariable,
            logger: sp.GetService<ILogger<AcpAgentSession>>()));
        builder.AddAgentProvider(sp => new AgentProvider(AcpAgentDoor.Describe(id, accountHomeVariable),
            _ => sp.GetRequiredKeyedService<IAgentSession>(id)));
        return builder;
    }
}
