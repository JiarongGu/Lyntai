namespace Lyntai.Agents;

/// <summary>What <see cref="AgentAccountPool"/> walks and how long it holds a seat for one attempt
/// (<c>AddAgentAccountPool</c>). Read once when the pool is built.</summary>
public sealed class AgentPoolOptions
{
    /// <summary>The ordered choices, at least one. A choice's accounts are walked before the next choice. An account
    /// is identified by its <see cref="AgentAccount.Provider"/> (its key) and <see cref="AgentAccount.Name"/>: the
    /// same name in two choices is the same account and must carry the same <see cref="AgentAccount.Home"/>. An
    /// account's key is its choice's provider or the agent that provider shares
    /// (<see cref="AgentProviderDescriptor.Agent"/>); listing one account in the choices of several providers of that
    /// agent is how one account serves every door, and a restriction on it holds in all of them.</summary>
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

    /// <param name="accountKeys">Each registered provider's account key; a provider not listed files its accounts
    /// under its own id.</param>
    /// <param name="checkOwners">False where the providers are not known yet (the builder's call): an account's
    /// relation to its choice's provider is then checked when the pool is built.</param>
    internal void Validate(IReadOnlyDictionary<string, string>? accountKeys, bool checkOwners)
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
                ArgumentException.ThrowIfNullOrWhiteSpace(account.Provider, nameof(Choices));
                // an account is filed under its key: the choice's provider, or the agent that provider shares
                var key = accountKeys is not null && accountKeys.TryGetValue(choice.Provider, out var shared) ? shared : choice.Provider;
                if (checkOwners && !string.Equals(account.Provider, choice.Provider, StringComparison.Ordinal)
                    && !string.Equals(account.Provider, key, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Account '{account.Name}' belongs to '{account.Provider}', but the choice is of provider '{choice.Provider}'" +
                        (key == choice.Provider ? "." : $" (agent '{key}')."),
                        nameof(Choices));
                if (!homes.TryAdd((account.Provider, account.Name), account.Home)
                    && !string.Equals(homes[(account.Provider, account.Name)], account.Home, StringComparison.Ordinal))
                    throw new ArgumentException(
                        $"Account '{account.Name}' of '{account.Provider}' is listed with two different homes.", nameof(Choices));
            }
        }
    }
}
