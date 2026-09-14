using Lyntai.Agents;
using Lyntai.Processes;
using Lyntai.Providers.CodexCli;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Lives in the Lyntai namespace so `AddCodexCliProvider` shows up right on the builder.
namespace Lyntai;

public static class CodexCliBuilderExtensions
{
    /// <summary>Register the OpenAI `codex` CLI provider (id "codex-cli"). With no arguments the spawned
    /// command honors <c>LYNTAI_PROVIDER_CMD</c> / <c>CODEX_CMD</c> env overrides (tests/e2e point these at a
    /// deterministic stub), then falls back to <c>codex</c> on PATH. If an <see cref="ICliToolProvisioner"/> is
    /// registered for this provider id, the CLI is given the app's registered tools over MCP.</summary>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="command">A PORTABLE install: the path to a <c>codex</c> the app ships or unpacks itself
    /// instead of a global one (quote a path with spaces). Read it from your own configuration and pass it
    /// here — no process-wide environment variable needed.</param>
    /// <param name="environment">Extra environment variables for every spawn; a portable install usually wants
    /// its own <c>CODEX_HOME</c> so it neither reads nor mutates the machine-wide install's state.</param>
    /// <param name="dialect">A pre-configured <see cref="CodexCliDialect"/> — e.g.
    /// <c>new CodexCliDialect { SandboxMode = "workspace-write" }</c> to let codex act on disk. Defaults to a
    /// read-only sandbox, which is what a text completion should need.</param>
    public static LyntaiBuilder AddCodexCliProvider(
        this LyntaiBuilder builder,
        string? command = null,
        IReadOnlyDictionary<string, string>? environment = null,
        CodexCliDialect? dialect = null)
    {
        builder.AddProvider(sp => new CodexCliProvider(
            sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<CodexCliProvider>>(),
            command,
            ResolveProvisioner(sp),
            environment,
            dialect));
        return builder;
    }

    /// <summary>Prefer the provisioner registered for THIS provider's id — several CLI providers can each host
    /// tools with their own dialect, and an unkeyed lookup would hand us whichever was registered first. The
    /// unkeyed service stays as the fallback so a hand-rolled <see cref="ICliToolProvisioner"/> registration
    /// (no dialect, no key) keeps working.</summary>
    private static ICliToolProvisioner? ResolveProvisioner(IServiceProvider sp) =>
        sp.GetKeyedService<ICliToolProvisioner>(CodexCliProvider.ProviderId)
        ?? sp.GetService<ICliToolProvisioner>();

    /// <summary>Register <see cref="CodexAgentSession"/> as an <see cref="IAgentSession"/> — the self-driving
    /// (agentic) codex seam, which streams the agent's TOOL STEPS rather than collapsing a turn to text.
    /// Registered both unkeyed and KEYED by <c>"codex-cli"</c>, so an app offering both CLI backends resolves
    /// the one it means (<c>GetRequiredKeyedService&lt;IAgentSession&gt;("codex-cli")</c>) instead of whichever
    /// registration happened to be last. The spawned command honors <c>LYNTAI_PROVIDER_CMD</c> /
    /// <c>CODEX_CMD</c> so tests and e2e can point at a deterministic stub.</summary>
    /// <remarks>Read <see cref="CodexAgentSession"/>'s remarks before adopting: codex's message/usage/terminal
    /// events are measured, its tool-step events are inferred, and three neutral
    /// <see cref="AgentSessionOptions"/> members (resume, disallowed tools, system prompt) are handled
    /// explicitly because codex has no equivalent.</remarks>
    /// <param name="builder">The Lyntai builder.</param>
    /// <param name="command">A PORTABLE <c>codex</c> path, as with
    /// <see cref="AddCodexCliProvider"/> — pass the same value to both so a host's bundled CLI is used for
    /// completions and agent sessions alike.</param>
    /// <param name="environment">Extra environment variables for the spawn; a portable install usually wants
    /// its own <c>CODEX_HOME</c>.</param>
    public static LyntaiBuilder AddCodexCliAgentSession(
        this LyntaiBuilder builder,
        string? command = null,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        builder.Services.AddSingleton<IAgentSession>(sp => CreateSession(sp, command, environment));
        builder.Services.AddKeyedSingleton<IAgentSession>(CodexCliProvider.ProviderId,
            (sp, _) => CreateSession(sp, command, environment));
        return builder;
    }

    private static CodexAgentSession CreateSession(
        IServiceProvider sp, string? command, IReadOnlyDictionary<string, string>? environment) =>
        new(sp.GetRequiredService<IProcessRunner>(),
            sp.GetRequiredService<LyntaiOptions>(),
            sp.GetService<ILogger<CodexAgentSession>>(),
            command,
            environment);
}
