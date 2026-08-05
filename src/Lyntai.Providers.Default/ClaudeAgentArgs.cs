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
    /// send it over stdin via <see cref="Lyntai.Processes.IProcessRunner.StreamLinesAsync"/>.
    ///
    /// <para><see cref="AgentSessionOptions.McpServers"/> is assumed already validated by the caller
    /// (<see cref="AgentMcpServers.TryValidate"/>, which the session runs so a refusal carries its own
    /// subtype) — this method renders, it does not judge.</para></summary>
    /// <param name="options">The turn.</param>
    /// <param name="writeTempFile">Given a short <c>kind</c> tag and the file's content, writes a config
    /// file and returns its path. Supplied by the caller so the paths can be tracked and deleted when the
    /// turn ends — and so a test can assert the file's contents without touching the disk.</param>
    public static IReadOnlyList<string> Build(AgentSessionOptions options, Func<string, string, string> writeTempFile)
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
        var claudeOptions = options as ClaudeAgentOptions;
        if (claudeOptions is { } c)
        {
            if (!string.IsNullOrEmpty(c.SettingsPath))
            {
                args.Add("--settings");
                args.Add(c.SettingsPath);
            }
            if (!bypass && c.AllowedTools.Count > 0)
            {
                args.Add("--allowedTools");
                args.Add(string.Join(",", c.AllowedTools));
            }
        }

        // MEASURED (`claude --help`, 2026-08-05): `--mcp-config <configs...>` — "Load MCP servers from JSON
        // files or strings (space-separated)". Because it takes a LIST, the neutral McpServers can be
        // rendered ALONGSIDE a caller's own McpConfigPath rather than displacing it: one flag, the caller's
        // document first. (If both declare a server under the same NAME, which one the CLI keeps is its
        // business and is not measured here — give them distinct names.) The rendered document goes to a
        // file rather than the inline-string form the flag also accepts, because a config can carry a bearer
        // token or a secret env value and argv is readable machine-wide.
        List<string> mcpConfigs = [];
        if (claudeOptions?.McpConfigPath is { Length: > 0 } callerConfig) mcpConfigs.Add(callerConfig);
        if (options.McpServers.Count > 0)
            mcpConfigs.Add(writeTempFile("mcp", ClaudeMcpConfig.Json(options.McpServers)));
        if (mcpConfigs.Count > 0)
        {
            args.Add("--mcp-config");
            args.AddRange(mcpConfigs);
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
