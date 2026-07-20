using Lyntai.Agents;

namespace Lyntai.Providers.ClaudeCli;

/// <summary>Builds the static argv for a <c>claude</c> self-driving agent session call.
/// The prompt always travels over stdin, never argv.</summary>
public static class ClaudeAgentArgs
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
        var args = new List<string>
        {
            "-p",
            "--output-format", "stream-json",
            "--verbose",
            "--include-partial-messages",
        };

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

        if (options.ToolPolicy == AgentToolPolicy.Write)
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
            if (c.AllowedTools.Count > 0)
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
