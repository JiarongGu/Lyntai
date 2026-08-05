using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Builds the static argv for a <c>claude</c> self-driving agent session call.
/// The prompt always travels over stdin, never argv.</summary>
internal static class ClaudeAgentArgs
{
    // Always denied in a headless (self-driving) run — these require interactive user input and
    // would hang the process.
    private static readonly string[] AlwaysDenied = ["AskUserQuestion", "ExitPlanMode", "EnterPlanMode"];

    // Denied when the caller opts into ReadOnly policy (no filesystem writes).
    private static readonly string[] ReadOnlyDenied = ["Edit", "Write", "NotebookEdit"];

    /// <summary>Build the argv list for a claude agent session. The prompt is NOT included here —
    /// send it over stdin via <see cref="Lyntai.Processes.IProcessRunner.StreamLinesAsync"/>.</summary>
    public static IReadOnlyList<string> Build(AgentSessionOptions options)
    {
        // the print-mode prefix is declared once, on ClaudeArgs — this path only adds the partial-message
        // events the agent reader needs on top of it
        var args = new List<string>(ClaudeArgs.PrintMode) { "--include-partial-messages" };

        // Build de-duplicated disallowed list: always-denied + caller-provided + (if ReadOnly) write tools
        var disallowed = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var t in AlwaysDenied)
        {
            if (seen.Add(t)) disallowed.Add(t);
        }
        foreach (var t in options.DisallowedTools)
        {
            if (seen.Add(t)) disallowed.Add(t);
        }
        if (options.ToolPolicy == AgentToolPolicy.ReadOnly)
        {
            foreach (var t in ReadOnlyDenied)
            {
                if (seen.Add(t)) disallowed.Add(t);
            }
        }

        if (disallowed.Count > 0)
        {
            args.Add("--disallowed-tools");
            args.Add(string.Join(",", disallowed));
        }

        // Headless bypass (CLI1): an agent on the user's own machine against the user's own resources needs
        // to skip ALL prompts (in `-p` there is no responder — a prompt hangs the turn). --dangerously-skip-
        // permissions REPLACES --permission-mode / --allowedTools (the CLI rejects combining them); the
        // --disallowed-tools denial above (always-denied flow tools + caller-supplied + ReadOnly writes) stands.
        var bypass = options is ClaudeAgentOptions { SkipAllPermissions: true };
        if (bypass)
        {
            args.Add("--dangerously-skip-permissions");
        }
        else if (options.ToolPolicy == AgentToolPolicy.Write)
        {
            args.Add("--permission-mode");
            args.Add("acceptEdits");
        }

        if (!string.IsNullOrEmpty(options.SystemPrompt))
        {
            args.Add("--append-system-prompt");
            args.Add(options.SystemPrompt);
        }

        // Claude-specific options (only when given a ClaudeAgentOptions)
        if (options is ClaudeAgentOptions c)
        {
            if (!string.IsNullOrEmpty(c.SettingsPath))
            {
                args.Add("--settings");
                args.Add(c.SettingsPath);
            }
            if (!string.IsNullOrEmpty(c.McpConfigPath))
            {
                args.Add("--mcp-config");
                args.Add(c.McpConfigPath);
            }
            if (!bypass && c.AllowedTools.Count > 0)
            {
                args.Add("--allowedTools");
                args.Add(string.Join(",", c.AllowedTools));
            }
        }

        if (!string.IsNullOrEmpty(options.ResumeToken))
        {
            args.Add("--resume");
            args.Add(options.ResumeToken);
        }

        if (!string.IsNullOrEmpty(options.Model))
        {
            args.Add("--model");
            args.Add(options.Model);
        }

        return args;
    }
}
