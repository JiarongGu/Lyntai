using Lyntai.Agents;

namespace Lyntai.Providers.Basic;

/// <summary>The <see cref="AgentSessionOptions.ResumeToken"/> check every CLI agent session in this package
/// shares, so they refuse the SAME tokens for the same reason: the token is opaque, free-form data in a data
/// slot, and a value the CLI would parse as an OPTION is refused before anything is spawned.
/// <c>ArgumentList</c> stops shell injection, not the backend's own parser. On codex a token such as
/// <c>--last</c> resumes the WRONG thread; on claude <c>--resume</c> takes an OPTIONAL value (measured,
/// <c>claude --help</c> 2.1.281), so <c>--dangerously-skip-permissions</c> would be read as that flag.</summary>
internal static class AgentResumeToken
{
    /// <summary>The <see cref="SessionEnded.Subtype"/> of a refused token — the TOKEN is bad, not the
    /// capability.</summary>
    internal const string RefusedSubtype = "resume-token-invalid";

    /// <summary>Read a resume token as a session id: the trimmed token, or false with a caller-actionable
    /// <paramref name="refusal"/> when it is blank or starts with <c>-</c>. A caller that treats an empty token
    /// as "start fresh" checks for that BEFORE calling.</summary>
    internal static bool TryRead(string? token, string backend, out string sessionId, out string? refusal)
    {
        sessionId = token?.Trim() ?? "";
        if (sessionId.Length > 0 && sessionId[0] != '-')
        {
            refusal = null;
            return true;
        }

        refusal = $"'{token}' is not a {backend} session id — a resume token that is blank, or that starts " +
            "with '-', would be read by the CLI as an OPTION rather than as the id it names. Pass the " +
            "SessionId reported by SessionStarted/SessionEnded, or null to start a fresh session.";
        sessionId = "";
        return false;
    }
}
