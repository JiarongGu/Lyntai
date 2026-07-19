namespace Lyntai.Providers.ClaudeCli;

/// <summary>Shared claude-CLI command resolution for both the provider and the agent session: the
/// override/env precedence (ctor override → <c>LYNTAI_PROVIDER_CMD</c> → <c>CLAUDE_CMD</c> → <c>claude</c>)
/// and the quote-aware tokenizer (so <c>node "C:\some dir\stub.mjs"</c> splits into exe + prefix args).
/// One home, so the precedence lives in a single place instead of being copied per call site.</summary>
internal static class ClaudeCommand
{
    /// <summary>Resolve <paramref name="command"/> (or the env seams, or a plain <c>claude</c>) into the
    /// executable + any prefix args (e.g. the stub script passed to <c>node</c>).</summary>
    public static (string Exe, IReadOnlyList<string> PrefixArgs) Resolve(string? command)
    {
        var cmd = command
            ?? Environment.GetEnvironmentVariable("LYNTAI_PROVIDER_CMD")
            ?? Environment.GetEnvironmentVariable("CLAUDE_CMD")
            ?? "claude";
        var tokens = Tokenize(cmd);
        return tokens.Count == 0 ? ("claude", []) : (tokens[0], tokens.Skip(1).ToList());
    }

    /// <summary>Split a command line into tokens, honoring double-quoted spans (paths with spaces).
    /// Only DOUBLE quotes are interpreted — single quotes and backslash escapes are treated literally, so a
    /// <c>LYNTAI_PROVIDER_CMD</c>/<c>CLAUDE_CMD</c> override should quote paths with <c>"</c> and avoid
    /// shell-style single-quote/escape syntax.</summary>
    public static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new System.Text.StringBuilder();
        var inQuotes = false;
        foreach (var c in commandLine)
        {
            if (c == '"') { inQuotes = !inQuotes; continue; }
            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (current.Length > 0) { tokens.Add(current.ToString()); current.Clear(); }
                continue;
            }
            current.Append(c);
        }
        if (current.Length > 0) tokens.Add(current.ToString());
        return tokens;
    }
}
