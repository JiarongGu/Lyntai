namespace Lyntai.Providers.ClaudeCli;

/// <summary>How a one-shot <c>claude</c> completion is spawned beyond its print-mode argv — one entry of
/// <see cref="ClaudeCliBackend.CompletionByConsumer"/>, so a scorer, a memory judge and an untagged utility call can
/// each be spawned differently. Every default leaves the argv exactly as it is without an entry.</summary>
public sealed record ClaudeCompletionOptions
{
    /// <summary><c>--disallowed-tools</c>: tools denied on top of <c>AskUserQuestion</c>, which a completion always
    /// denies — a caller ADDS denials and never removes that one. A print-mode call with no permission host still
    /// runs the read-only and permission-free tools, so <c>["PowerShell", "Monitor"]</c>, say, keeps a judge that
    /// should only read from running a shell. Empty, the default, denies <c>AskUserQuestion</c> alone. Copied when
    /// set.</summary>
    /// <exception cref="ArgumentNullException">The value is null.</exception>
    public IReadOnlyList<string> DisallowedTools
    {
        get;
        init => field = value is null ? throw new ArgumentNullException(nameof(DisallowedTools)) : [.. value];
    } = [];

    /// <summary><c>--settings</c>: a settings file the call is handed, as <see cref="ClaudeAgentOptions.SettingsPath"/>
    /// hands one to an agent run. A command-line settings file outranks the project and local scopes and applies under
    /// every <see cref="SettingSources"/> value, so settings a host needs on EVERY call reach its one-shot calls too —
    /// <c>disableSkillShellExecution</c>, say, or a blanked <c>apiKeyHelper</c> a project file cannot re-enable. Null or
    /// empty, the default, omits the flag. Give a full path: a relative one resolves against the call's working
    /// directory, not the host process's.
    /// <para><b>The CLI applies only the LAST <c>--settings</c></b>, so a call a tool host serves is handed ONE file:
    /// this one merged into the host's per-call allow-list — objects key by key, lists joined, this file's value winning
    /// a clash. A file that cannot be merged — missing, not one JSON object, a relative path — is handed after the
    /// host's, unchanged, so the CLI treats it as it would with no host.</para></summary>
    public string? SettingsPath { get; init; }

    /// <summary><c>--setting-sources</c>: which of the CLI's sources the call loads — <c>user</c>, <c>project</c>,
    /// <c>local</c>. Null, the default, omits the flag and the CLI loads all three. Each source is more than its
    /// settings file: <c>project</c> is also the project <c>CLAUDE.md</c> — one in a PARENT of the working directory
    /// too, since the CLI walks up for it — with its rules, skills and <c>.mcp.json</c>. An EMPTY list loads no source;
    /// a command-line settings file applies under every value. An unknown name is the CLI's to refuse, which it does
    /// before any turn. Copied when set.</summary>
    /// <exception cref="ArgumentException">An entry is null, empty, flag-shaped, or holds a comma or
    /// whitespace.</exception>
    public IReadOnlyList<string>? SettingSources
    {
        get;
        init => field = ClaudeArgs.CheckSettingSources(value, nameof(SettingSources));
    }

    /// <summary><c>--strict-mcp-config</c>: start only the MCP servers the call is handed — a tool host's included —
    /// and none from a project <c>.mcp.json</c>. False, the default, omits the flag.</summary>
    public bool StrictMcpConfig { get; init; }
}
