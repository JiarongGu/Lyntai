namespace Lyntai.Providers.CodexCli;

/// <summary>The ONE place that knows how to ask the <c>codex</c> CLI for a non-interactive turn. Both codex
/// seams build their argv here — the text completion (<see cref="CodexCliDialect.BuildCompletionArgs"/>,
/// through <see cref="Lyntai.Llm.Cli.CliProviderEngine"/>) and the self-driving agent session
/// (<see cref="CodexAgentSession"/>) — so a flag can never be present on one path and missing from the other.
///
/// <para>That is not tidiness. <c>--skip-git-repo-check</c> is the flag a consuming app's hand-rolled codex
/// integration dropped: codex refuses to run outside a git repository, so omitting it WORKS on a developer's
/// machine (a repo) and breaks in a shipped bundle (not one). A second copy of this argv is a second chance
/// to lose it. The RESUME path added here is a third chance, which is why it is a branch inside this class
/// rather than an argv of its own.</para>
///
/// Every flag below was MEASURED against codex-cli 0.146.0 (2026-08-04; the <c>exec resume</c> shape
/// 2026-08-05) — see <see cref="CodexCliDialect"/>'s remarks for the measurement and for why a GUESSED
/// subcommand is dangerous on this CLI (<c>codex [OPTIONS] [PROMPT]</c> reads an unrecognized subcommand as a
/// prompt and spends a turn on it).</summary>
internal static class CodexExecArgs
{
    /// <summary>MEASURED sandbox value: the agent may read the working tree but not write to it.</summary>
    public const string ReadOnlySandbox = "read-only";

    /// <summary>MEASURED sandbox value: the agent may write inside its workspace.</summary>
    public const string WorkspaceWriteSandbox = "workspace-write";

    /// <summary>Build <c>codex exec --json … -</c> for a FRESH thread: non-interactive, JSONL events on
    /// stdout, prompt from stdin.</summary>
    /// <param name="sandboxMode">The <c>--sandbox</c> value (<see cref="ReadOnlySandbox"/>,
    /// <see cref="WorkspaceWriteSandbox"/>, or <c>danger-full-access</c>).</param>
    /// <param name="model">The model to request, or null/empty for the CLI's own default.</param>
    public static List<string> Build(string sandboxMode, string? model) => BuildArgv(sandboxMode, model, null);

    /// <summary>Build <c>codex exec resume … &lt;SESSION_ID&gt; -</c> — the same turn against an EXISTING
    /// thread — or REFUSE the token without building anything.
    ///
    /// <para>MEASURED against codex-cli 0.146.0 (2026-08-05, via <c>codex exec resume --help</c>, which is a
    /// FLAG and therefore costs no turn): <c>Usage: codex exec resume [OPTIONS] [SESSION_ID] [PROMPT]</c>,
    /// where <c>[SESSION_ID]</c> is the "Conversation/session id (UUID) or thread name" and <c>[PROMPT]</c> is
    /// the same slot the fresh path fills with <c>-</c> ("If <c>-</c> is used, read from stdin"). Two things
    /// that measurement settles and a guess could not: <c>resume</c> is a real SUBCOMMAND of <c>exec</c> — an
    /// unrecognized one would have been read as a PROMPT and silently spent a turn — and the id is a
    /// POSITIONAL that must therefore precede the <c>-</c>, which is where the guess would most likely have
    /// gone wrong. The flags around it are clap options: a wrong one errors out, it does not bill. The one
    /// thing measurement does NOT settle is where the OPTIONS sit relative to the id, so the argv follows the
    /// CLI's own usage line (options, then id, then <c>-</c>) — see <c>BuildArgv</c>.</para>
    ///
    /// <para>The token is free-form and opaque to Lyntai, so it is REFUSED when codex would read it as an
    /// OPTION instead of an id — anything starting with <c>-</c>, notably this subcommand's own <c>--last</c>
    /// ("Resume the most recent recorded session"), which would quietly resume the WRONG thread, and
    /// <c>-i &lt;file&gt;</c>, which would eat the next argument. Values travel as separate argument-list
    /// entries, never through a shell, so this is not shell injection — it is the backend's own parser
    /// reading a data slot as an option (same guard as <c>ClaudeCliDialect</c>'s version/email slots).</para></summary>
    /// <param name="sandboxMode">As <see cref="Build"/>.</param>
    /// <param name="model">As <see cref="Build"/>.</param>
    /// <param name="resumeToken">The caller's opaque resume handle (a prior run's session id).</param>
    /// <param name="args">The argv, or empty when refused.</param>
    /// <param name="refusal">Why the token cannot be sent, or null when it can.</param>
    public static bool TryBuildResume(
        string sandboxMode, string? model, string? resumeToken, out List<string> args, out string? refusal)
    {
        var sessionId = resumeToken?.Trim();
        if (sessionId is not { Length: > 0 } || sessionId[0] == '-')
        {
            args = [];
            refusal = $"'{resumeToken}' is not a codex session id — a resume token that is blank, or that " +
                "starts with '-', would be read by the CLI as an OPTION in the positional [SESSION_ID] slot " +
                "(its own --last would resume the most recent thread instead of yours). Pass the SessionId " +
                "reported by SessionStarted/SessionEnded, or null to start a fresh session.";
            return false;
        }

        args = BuildArgv(sandboxMode, model, sessionId);
        refusal = null;
        return true;
    }

    /// <summary>The argv both paths share; <paramref name="sessionId"/> null = a fresh thread. Only
    /// <see cref="TryBuildResume"/> may supply an id, so an unusable one can never reach argv.</summary>
    private static List<string> BuildArgv(string sandboxMode, string? model, string? sessionId)
    {
        // MEASURED: `resume` is a SUBCOMMAND of `exec`, so it goes immediately after it — nowhere else.
        List<string> args = sessionId is { Length: > 0 } ? ["exec", "resume"] : ["exec"];

        // Everything from here to `-` is deliberately identical on both paths: a RESUMED turn needs
        // `--skip-git-repo-check` for exactly the same reason a fresh one does, and this class exists so that
        // cannot be true on one path and forgotten on the other.
        args.Add("--json");                    // measured: "Print events to stdout as JSONL"
        args.Add("--skip-git-repo-check");     // measured: codex refuses to run outside a git repository
        args.Add("--color");                   // keep ANSI escapes out of anything we read
        args.Add("never");
        args.Add("--sandbox");
        args.Add(sandboxMode);

        if (model is { Length: > 0 })
        {
            args.Add("--model");
            args.Add(model);
        }

        // MEASURED: `codex exec resume [OPTIONS] [SESSION_ID] [PROMPT]` — the id is the FIRST positional and
        // the `-` below is the SECOND, so the id belongs here: after every option, immediately before the
        // stdin marker. Options-then-positionals is also the only invocation shape of this CLI that has ever
        // actually been RUN (the fresh argv, measured), and it is the order its own usage line prints;
        // putting the id ahead of the flags would look equally plausible and would additionally be at the
        // mercy of whether the [PROMPT] positional accepts hyphen-leading values (it would then swallow every
        // flag after the id as prompt text — and on this CLI a swallowed flag is a spent turn, not an error).
        if (sessionId is { Length: > 0 }) args.Add(sessionId);

        // measured: "If not provided as an argument (or if `-` is used), instructions are read from stdin".
        // Explicit `-` rather than relying on the omitted-argument default — the same reasoning as passing
        // claude's `--json` explicitly. On the resume path it lands in [PROMPT], after [SESSION_ID].
        args.Add("-");
        return args;
    }
}
