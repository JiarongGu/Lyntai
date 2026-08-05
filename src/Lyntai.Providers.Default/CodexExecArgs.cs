namespace Lyntai.Providers.CodexCli;

/// <summary>The ONE place that knows how to ask the <c>codex</c> CLI for a non-interactive turn. Both codex
/// seams build their argv here — the text completion (<see cref="CodexCliDialect.BuildCompletionArgs"/>,
/// through <see cref="Lyntai.Llm.Cli.CliProviderEngine"/>) and the self-driving agent session
/// (<see cref="CodexAgentSession"/>) — so a flag can never be present on one path and missing from the other.
///
/// <para>That is not tidiness. <c>--skip-git-repo-check</c> is the flag a consuming app's hand-rolled codex
/// integration dropped: codex refuses to run outside a git repository, so omitting it WORKS on a developer's
/// machine (a repo) and breaks in a shipped bundle (not one). A second copy of this argv is a second chance
/// to lose it.</para>
///
/// Every flag below was MEASURED against codex-cli 0.146.0 (2026-08-04) — see
/// <see cref="CodexCliDialect"/>'s remarks for the measurement and for why a GUESSED subcommand is dangerous
/// on this CLI (<c>codex [OPTIONS] [PROMPT]</c> reads an unrecognized subcommand as a prompt and spends a
/// turn on it).</summary>
internal static class CodexExecArgs
{
    /// <summary>MEASURED sandbox value: the agent may read the working tree but not write to it.</summary>
    public const string ReadOnlySandbox = "read-only";

    /// <summary>MEASURED sandbox value: the agent may write inside its workspace.</summary>
    public const string WorkspaceWriteSandbox = "workspace-write";

    /// <summary>Build <c>codex exec --json … -</c>: non-interactive, JSONL events on stdout, prompt from
    /// stdin.</summary>
    /// <param name="sandboxMode">The <c>--sandbox</c> value (<see cref="ReadOnlySandbox"/>,
    /// <see cref="WorkspaceWriteSandbox"/>, or <c>danger-full-access</c>).</param>
    /// <param name="model">The model to request, or null/empty for the CLI's own default.</param>
    public static List<string> Build(string sandboxMode, string? model)
    {
        List<string> args =
        [
            "exec",
            "--json",                      // measured: "Print events to stdout as JSONL"
            "--skip-git-repo-check",       // measured: codex refuses to run outside a git repository
            "--color", "never",            // keep ANSI escapes out of anything we read
            "--sandbox", sandboxMode,
        ];
        if (model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(model);
        }
        // measured: "If not provided as an argument (or if `-` is used), instructions are read from stdin".
        // Explicit `-` rather than relying on the omitted-argument default — the same reasoning as passing
        // claude's `--json` explicitly.
        args.Add("-");
        return args;
    }
}
