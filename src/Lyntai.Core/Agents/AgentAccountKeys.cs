namespace Lyntai.Agents;

/// <summary>The one rule on providers that share an account key: they read the account's home through the same
/// variable. The registry applies it to everything registered and the builder to what it is told at the call.</summary>
internal static class AgentAccountKeys
{
    /// <summary>Refuses a provider that states an <see cref="AgentProviderDescriptor.Agent"/> and reads no account, and
    /// providers sharing a key that read the home through different variables.</summary>
    /// <exception cref="InvalidOperationException">Either; the message names the providers and the variables.</exception>
    internal static void Validate(IEnumerable<AgentProviderDescriptor> descriptors)
    {
        var first = new Dictionary<string, AgentProviderDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in descriptors)
        {
            if (descriptor.Accounts is null)
            {
                if (descriptor.Agent is not null)
                    throw new InvalidOperationException(
                        $"Agent provider '{descriptor.Id}' names agent '{descriptor.Agent}' but reads no account: " +
                        "sharing an agent's accounts needs the variable its program reads the home from.");
                continue;
            }
            if (!first.TryAdd(descriptor.AccountKey, descriptor)
                && !string.Equals(first[descriptor.AccountKey].Accounts!.HomeVariable, descriptor.Accounts.HomeVariable, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Providers '{first[descriptor.AccountKey].Id}' and '{descriptor.Id}' share the accounts of '{descriptor.AccountKey}' " +
                    $"but read the home from different variables ({first[descriptor.AccountKey].Accounts!.HomeVariable} and " +
                    $"{descriptor.Accounts.HomeVariable}), so one of them would be handed a home its program does not read. " +
                    "Register the one that differs under an agent of its own.");
        }
    }
}
