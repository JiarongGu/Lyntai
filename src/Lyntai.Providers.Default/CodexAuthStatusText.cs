using System.Text.RegularExpressions;
using Lyntai.Llm;

namespace Lyntai.Providers.CodexCli;

/// <summary>Reads what <c>codex login status</c> says about the CLI's credentials.
///
/// Unlike the claude CLI, codex has **no machine-readable auth readout** — measured on codex-cli 0.146.0
/// (2026-08-04): <c>codex login status --json</c> is rejected (<c>error: unexpected argument '--json'</c>) and
/// the command prints prose. Signed-out is exactly <c>"Not logged in"</c> with exit code 0. So this is
/// deliberately a PROSE sniffer, which is why <see cref="Llm.Cli.ICliProviderDialect.ParseAuthStatus"/> takes
/// raw text rather than assuming JSON.
///
/// Conservative by construction: an explicit negative marker means signed out, an explicit positive marker
/// means signed in, and anything else returns <c>null</c> — "unknown". The engine then reports
/// <c>Authenticated: false</c> with the CLI's own words in the detail, so an unrecognized wording can never
/// be mistaken for a signed-in state.</summary>
/// <remarks>UNVERIFIED CORNER: only the signed-OUT wording could be measured here (this machine's codex is not
/// logged in, and signing in requires a real account + browser flow). If a future build's signed-in line
/// doesn't match <see cref="SignedInPattern"/>, status degrades to "unknown" with the raw text — never to a
/// wrong answer. Widen the pattern once a signed-in line has actually been observed.</remarks>
internal static partial class CodexAuthStatusText
{
    public static ProviderAuthStatus? Parse(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;

        // negative FIRST: "Not logged in" also contains "logged in"
        if (SignedOutPattern().IsMatch(output)) return new ProviderAuthStatus(false);
        if (!SignedInPattern().IsMatch(output)) return null; // unknown wording — say nothing

        var method = MethodPattern().Match(output);
        var account = EmailPattern().Match(output);
        return new ProviderAuthStatus(true,
            Method: method.Success ? method.Groups[1].Value.Trim() : null,
            Account: account.Success ? account.Value : null);
    }

    /// <summary>Measured exactly: <c>Not logged in</c>. The alternatives are defensive.</summary>
    [GeneratedRegex(@"not\s+(?:logged\s+in|signed\s+in|authenticated)|logged\s+out|no\s+credentials", RegexOptions.IgnoreCase)]
    private static partial Regex SignedOutPattern();

    [GeneratedRegex(@"logged\s+in|signed\s+in|authenticated\s+(?:as|with|using)", RegexOptions.IgnoreCase)]
    private static partial Regex SignedInPattern();

    /// <summary>How it is signed in, when the line names a mechanism (an API key vs. an account).</summary>
    [GeneratedRegex(@"(?:using|with|via)\s+(?:an?\s+)?([A-Za-z][A-Za-z0-9 ._-]{2,30}?)(?=\s+(?:account|credentials?|key)\b|[.,:\r\n]|$)", RegexOptions.IgnoreCase)]
    private static partial Regex MethodPattern();

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();
}
