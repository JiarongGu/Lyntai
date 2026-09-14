using Lyntai.Agents;

namespace Lyntai.Providers.CodexCli;

/// <summary>Turns neutral <see cref="AgentSessionOptions"/> into the codex invocation: the argv (through the
/// shared <see cref="CodexExecArgs"/>) and the single blob of text that travels over stdin.
///
/// <para>Two neutral options have no measured codex flag, and each is handled explicitly rather than
/// silently: <see cref="AgentSessionOptions.SystemPrompt"/> is PREPENDED to the prompt (see
/// <see cref="BuildPrompt"/>), and <see cref="AgentSessionOptions.DisallowedTools"/> is reported by
/// <see cref="CodexAgentSession"/> as unhonoured — codex's tool gate is the sandbox, not a deny list.
/// <see cref="AgentSessionOptions.ResumeToken"/> is no longer one of them: <c>exec resume</c> was measured on
/// 2026-08-05 and is honoured (see <see cref="CodexExecArgs.TryBuildResume"/>).</para></summary>
internal static class CodexAgentArgs
{
    /// <summary>Build the argv for a codex agent session, or REFUSE the turn. The prompt is NOT included — it
    /// travels on stdin (the trailing <c>-</c> in the argv is what tells codex to read it there).
    ///
    /// <para>The only refusal is a <see cref="AgentSessionOptions.ResumeToken"/> the CLI would read as an
    /// OPTION rather than as a session id; every other neutral option either maps or is reported as
    /// unhonoured. <see cref="AgentSessionOptions.McpServers"/> is assumed already validated by the caller
    /// (<see cref="AgentMcpServers.TryValidate"/>, which the session runs so a refusal carries its own
    /// subtype). Building the resume argv through <see cref="CodexExecArgs"/> is what keeps the resumed
    /// invocation flag-for-flag identical to the fresh one — the MCP overrides included, which is the whole
    /// reason they are passed INTO it rather than appended after.</para></summary>
    /// <param name="options">The turn.</param>
    /// <param name="args">The argv, or empty when refused.</param>
    /// <param name="environment">Environment variables the spawn must carry for the argv to mean what it
    /// says — currently the bearer tokens of HTTP MCP servers, which codex reads only from a NAMED variable
    /// (see <see cref="CodexMcpConfig"/>). Empty when there are none.</param>
    /// <param name="refusal">Why the turn cannot be spawned, or null when it can.</param>
    public static bool TryBuild(
        AgentSessionOptions options,
        out IReadOnlyList<string> args,
        out IReadOnlyDictionary<string, string> environment,
        out string? refusal)
    {
        var mcpArgs = CodexMcpConfig.Build(options.McpServers, out var mcpEnvironment);
        environment = mcpEnvironment;
        args = [];

        if (options.ResumeToken is { Length: > 0 })
        {
            var built = CodexExecArgs.TryBuildResume(
                SandboxFor(options), options.Model, options.ResumeToken, mcpArgs, out var resumeArgs, out refusal);
            args = resumeArgs;
            return built;
        }

        args = CodexExecArgs.Build(SandboxFor(options), options.Model, mcpArgs);
        refusal = null;
        return true;
    }

    /// <summary>The <c>--sandbox</c> value: an explicit <see cref="CodexAgentOptions.SandboxMode"/> wins,
    /// otherwise the neutral <see cref="AgentToolPolicy"/> maps onto codex's measured sandbox values. The
    /// mapping is Lyntai's choice; the VALUES are measured.</summary>
    private static string SandboxFor(AgentSessionOptions options) =>
        (options as CodexAgentOptions)?.SandboxMode is { Length: > 0 } explicitMode
            ? explicitMode
            : options.ToolPolicy == AgentToolPolicy.Write
                ? CodexExecArgs.WorkspaceWriteSandbox
                : CodexExecArgs.ReadOnlySandbox;

    /// <summary>The stdin payload. <c>codex exec</c> has no measured append-system-prompt flag, so a system
    /// prompt travels as a leading block of the prompt itself — the honest option, since inventing a flag
    /// would be read by <c>codex [OPTIONS] [PROMPT]</c> as an error and dropping the system prompt would
    /// silently change the turn. A caller that wants codex's own persistent instructions instead should use
    /// its <c>AGENTS.md</c>, which the CLI loads from the working directory.</summary>
    public static string BuildPrompt(AgentSessionOptions options) =>
        string.IsNullOrEmpty(options.SystemPrompt)
            ? options.Prompt
            : $"{options.SystemPrompt}\n\n{options.Prompt}";
}
