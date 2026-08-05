using Lyntai.Agents;

namespace Lyntai.Providers.CodexCli;

/// <summary>Turns neutral <see cref="AgentSessionOptions"/> into the codex invocation: the argv (through the
/// shared <see cref="CodexExecArgs"/>) and the single blob of text that travels over stdin.
///
/// <para>Two neutral options have no measured codex flag, and each is handled explicitly rather than
/// silently: <see cref="AgentSessionOptions.SystemPrompt"/> is PREPENDED to the prompt (see
/// <see cref="BuildPrompt"/>), and <see cref="AgentSessionOptions.DisallowedTools"/> is reported by
/// <see cref="CodexAgentSession"/> as unhonoured — codex's tool gate is the sandbox, not a deny list.</para></summary>
internal static class CodexAgentArgs
{
    /// <summary>Build the argv for a codex agent session. The prompt is NOT included — it travels on stdin
    /// (the trailing <c>-</c> in the argv is what tells codex to read it there).</summary>
    public static IReadOnlyList<string> Build(AgentSessionOptions options) =>
        CodexExecArgs.Build(SandboxFor(options), options.Model);

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
