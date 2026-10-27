using Lyntai.Agents;

namespace Lyntai.Providers.Basic;

/// <summary>How a CLI agent session turns <see cref="AgentSessionOptions.Account"/> into the child's environment,
/// so both doors read an account the same way: one variable carrying the host's configuration home, and nothing
/// else. The library never opens the home, creates it or copies out of it (<c>D20</c>).</summary>
internal static class AgentAccounts
{
    /// <summary>The <see cref="SessionEnded.Subtype"/> of a turn refused because the account cannot be handed to
    /// the child as given.</summary>
    internal const string RefusedSubtype = "account-invalid";

    /// <summary>The per-turn environment an account asks for: <paramref name="homeVariable"/> set to the home, or
    /// nothing when there is no account. False with a caller-actionable <paramref name="refusal"/> when the name
    /// or the home is blank, or the home holds a control character (the OS rejects such a value at spawn, and a
    /// NUL would cut it short).</summary>
    internal static bool TryMap(AgentAccount? account, string homeVariable,
        out IReadOnlyDictionary<string, string> environment, out string? refusal)
    {
        environment = new Dictionary<string, string>(0);
        refusal = null;
        if (account is null) return true;

        if (string.IsNullOrWhiteSpace(account.Name))
            refusal = "an account needs a name; pass the one the host calls it by.";
        else if (string.IsNullOrWhiteSpace(account.Home) || account.Home.Any(char.IsControl))
            refusal = $"account '{account.Name}' has no usable home: it must be a path with no control character.";
        if (refusal is not null) return false;

        environment = new Dictionary<string, string>(1, StringComparer.Ordinal) { [homeVariable] = account.Home };
        return true;
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
