namespace Lyntai.Agents;

/// <summary>What <see cref="AgentAccountPool"/> walks and how long it holds a seat for one attempt
/// (<c>AddAgentAccountPool</c>). Read once when the pool is built.</summary>
public sealed class AgentPoolOptions
{
    /// <summary>The ordered choices, at least one. A choice's accounts are walked before the next choice. An account
    /// is identified by its provider and <see cref="AgentAccount.Name"/>: the same name in two choices is the same
    /// account and must carry the same <see cref="AgentAccount.Home"/>.</summary>
    public List<AgentChoice> Choices { get; } = [];

    /// <summary>Names this pool in the key-value store, so two pools in one store do not read each other's holds.
    /// Default <c>"default"</c>.</summary>
    public string Name { get; set; } = "default";

    /// <summary>How long one attempt under a released restriction holds the seat against others before the pool
    /// assumes the attempt was lost (a crash, a host that never reported) and admits another. Default 30 minutes;
    /// it should outlast your longest turn, since a turn that runs past it no longer bars the next attempt.</summary>
    public TimeSpan AttemptLease { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>How long after a failure that states NO recovery the pool admits one probing attempt on its own.
    /// Null (the default) never does: an unknown recovery holds until the host clears it. Lyntai never invents a
    /// deadline, so a cool-off for an unstated reset is yours to choose (a 429 with no <c>retry-after</c> reads as
    /// unknown, and without this it holds its seat until you act). An owner action never expires.</summary>
    public TimeSpan? UnknownProbeAfter { get; set; }

    internal void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Name, nameof(Name));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(AttemptLease, TimeSpan.Zero, nameof(AttemptLease));
        if (UnknownProbeAfter is { } after)
            ArgumentOutOfRangeException.ThrowIfLessThan(after, TimeSpan.Zero, nameof(UnknownProbeAfter));
        if (Choices.Count == 0)
            throw new ArgumentException("An agent account pool needs at least one choice.", nameof(Choices));

        var homes = new Dictionary<(string, string), string>();
        foreach (var choice in Choices)
        {
            ArgumentNullException.ThrowIfNull(choice);
            ArgumentException.ThrowIfNullOrWhiteSpace(choice.Provider, nameof(Choices));
            if (choice.Accounts is null || choice.Accounts.Count == 0)
                throw new ArgumentException($"A choice of '{choice.Provider}' has no account.", nameof(Choices));
            foreach (var account in choice.Accounts)
            {
                ArgumentNullException.ThrowIfNull(account);
                ArgumentException.ThrowIfNullOrWhiteSpace(account.Name, nameof(Choices));
                if (!string.Equals(account.Provider, choice.Provider, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Account '{account.Name}' belongs to provider '{account.Provider}', but the choice is of '{choice.Provider}'.",
                        nameof(Choices));
                if (!homes.TryAdd((account.Provider, account.Name), account.Home)
                    && !string.Equals(homes[(account.Provider, account.Name)], account.Home, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Account '{account.Name}' of '{account.Provider}' is listed with two different homes.", nameof(Choices));
            }
        }
    }
}
