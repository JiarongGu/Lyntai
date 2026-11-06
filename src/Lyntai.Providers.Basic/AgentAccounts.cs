using Lyntai.Agents;

namespace Lyntai.Providers.Basic;

/// <summary>How a CLI agent session turns <see cref="AgentSessionOptions.Account"/> into the child's environment,
/// so every door reads an account the same way: the host's configuration home in the home variable and the
/// account's own entries beside it, each a name the provider reads. The library never opens the home, creates it or
/// copies out of it (<c>D20</c>), and a value reaches the environment and no message, so every refusal names a
/// variable and never a value.</summary>
internal static class AgentAccounts
{
    /// <summary>The <see cref="SessionEnded.Subtype"/> of a turn refused because the account cannot be handed to
    /// the child as given.</summary>
    internal const string RefusedSubtype = "account-invalid";

    /// <summary>The <see cref="SessionEnded.Subtype"/> of a turn refused because its own environment holds a name or value no
    /// child can be started with.</summary>
    internal const string EnvironmentRefusedSubtype = "environment-invalid";

    /// <summary>The per-turn environment an account asks for: the home variable set to the home, then the account's
    /// entries, or nothing when there is no account. False with a caller-actionable <paramref name="refusal"/> when
    /// the name is blank, the account has neither a home nor an entry, the home is blank or holds a control
    /// character (the OS rejects such a value at spawn, and a NUL would cut it short), an entry is named outside
    /// <see cref="AgentAccountMapping.Variables"/> or is the home variable, or an entry's value holds a control
    /// character.</summary>
    internal static bool TryMap(AgentAccount? account, AgentAccountMapping mapping,
        out IReadOnlyDictionary<string, string> environment, out string? refusal)
    {
        environment = new Dictionary<string, string>(0);
        refusal = null;
        if (account is null) return true;

        var entries = account.Environment ?? new Dictionary<string, string>(0);
        if (string.IsNullOrWhiteSpace(account.Name))
            refusal = "an account needs a name; pass the one the host calls it by.";
        else if (account.Home is null && entries.Count == 0)
            refusal = $"account '{account.Name}' has neither a home nor an environment entry.";
        else if (account.Home is not null && (string.IsNullOrWhiteSpace(account.Home) || account.Home.Any(char.IsControl)))
            refusal = $"account '{account.Name}' has no usable home: it must be a path with no control character.";
        else
        {
            foreach (var (name, value) in entries)
            {
                if (string.Equals(name, mapping.HomeVariable, StringComparison.Ordinal))
                    refusal = $"account '{account.Name}' sets {name} as an entry: the home variable is set by its Home.";
                else if (!mapping.Variables.Contains(name, StringComparer.Ordinal))
                    refusal = $"account '{account.Name}' sets '{name}', which the provider does not read " +
                        $"(it reads {string.Join(", ", mapping.Variables)}).";
                else if (value is null || value.Any(char.IsControl))
                    refusal = $"account '{account.Name}' has no usable value for {name}: it must be text with no control character.";
                if (refusal is not null) break;
            }
        }
        if (refusal is not null) return false;

        var set = new Dictionary<string, string>(entries.Count + 1, StringComparer.Ordinal);
        if (account.Home is not null) set[mapping.HomeVariable] = account.Home;
        foreach (var (name, value) in entries) set[name] = value;
        environment = set;
        return true;
    }

    /// <summary>The names a turn's own <see cref="AgentSessionOptions.Environment"/> may carry: any a child can have.
    /// False with a <paramref name="refusal"/> naming a variable and never a value.</summary>
    internal static bool TryCheckTurn(IReadOnlyDictionary<string, string?> environment, out string? refusal)
    {
        refusal = null;
        foreach (var (name, value) in environment)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || name.Any(char.IsControl))
                refusal = $"'{name}' is not a name an environment variable can have.";
            else if (value is not null && value.Any(char.IsControl))
                refusal = $"the value of {name} holds a control character.";
            if (refusal is not null) return false;
        }
        return true;
    }

    /// <summary>The standing environment with the turn's own variables over it; a null value is a removal and is carried.</summary>
    internal static IReadOnlyDictionary<string, string?>? MergeTurn(
        IReadOnlyDictionary<string, string?>? standing, IReadOnlyDictionary<string, string?> turn)
    {
        if (turn.Count == 0) return standing;
        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (standing is not null) foreach (var (key, value) in standing) merged[key] = value;
        foreach (var (key, value) in turn) merged[key] = value;
        return merged;
    }

    /// <summary>A session's standing environment plus what this TURN adds. The turn's entries win on a collision,
    /// a standing entry's null (a removal) is carried as it stands, and the standing dictionary is returned
    /// untouched when there is nothing to add.</summary>
    internal static IReadOnlyDictionary<string, string?>? Merge(
        IReadOnlyDictionary<string, string?>? standing, IReadOnlyDictionary<string, string> perTurn)
    {
        if (perTurn.Count == 0) return standing;

        var merged = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (standing is not null)
        {
            foreach (var (key, value) in standing) merged[key] = value;
        }
        foreach (var (key, value) in perTurn) merged[key] = value;
        return merged;
    }
}
