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
}
