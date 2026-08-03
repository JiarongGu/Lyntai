using System.Text;

namespace Lyntai.Llm.Cli;

/// <summary>Resolves WHICH command a CLI-backed provider spawns, and splits it into an executable plus
/// prefix args. One home for the precedence — an explicit override, then the environment variables the
/// dialect names, then its default executable — so it can't be re-derived (differently) per call site.</summary>
/// <remarks>The first environment variable a dialect names is conventionally
/// <c>LYNTAI_PROVIDER_CMD</c>: the shared seam that points tests and the e2e harness at the deterministic
/// provider stub instead of a real backend.</remarks>
public static class CliCommand
{
    /// <summary>Resolve the command to spawn.</summary>
    /// <param name="command">An explicit override (a ctor argument / configuration value). Wins over
    /// everything when non-blank.</param>
    /// <param name="defaultCommand">The backend's own executable name, used when nothing else answers.</param>
    /// <param name="environmentVariables">Variables to consult, in priority order.</param>
    /// <param name="environmentLookup">How to read a variable — defaults to the process environment.
    /// Injectable so precedence is testable without mutating global state.</param>
    /// <returns>The executable plus any prefix args (e.g. <c>node "stub.mjs"</c> → <c>node</c> +
    /// <c>["stub.mjs"]</c>), which every later argument is appended to.</returns>
    public static (string Exe, IReadOnlyList<string> PrefixArgs) Resolve(
        string? command,
        string defaultCommand,
        IReadOnlyList<string> environmentVariables,
        Func<string, string?>? environmentLookup = null)
    {
        var lookup = environmentLookup ?? Environment.GetEnvironmentVariable;
        var resolved = string.IsNullOrWhiteSpace(command) ? null : command;

        if (resolved is null)
        {
            foreach (var name in environmentVariables)
            {
                var value = lookup(name);
                if (!string.IsNullOrWhiteSpace(value)) { resolved = value; break; }
            }
        }

        var tokens = Tokenize(resolved ?? defaultCommand);
        return tokens.Count == 0 ? (defaultCommand, []) : (tokens[0], tokens.Skip(1).ToList());
    }

    /// <summary>Split a command line into tokens, honoring double-quoted spans (paths with spaces).
    /// Only DOUBLE quotes are interpreted — single quotes and backslash escapes are treated literally, so an
    /// override should quote paths with <c>"</c> and avoid shell-style single-quote/escape syntax.</summary>
    public static List<string> Tokenize(string commandLine)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
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
