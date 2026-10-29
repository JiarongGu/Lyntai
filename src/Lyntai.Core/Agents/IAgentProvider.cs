using Lyntai.Inference;
using System.Runtime.CompilerServices;

namespace Lyntai.Agents;

/// <summary>One agent a host can run: what it is, and a session per door. Registered as a collection and
/// resolved by id through <see cref="AgentProviderRegistry"/>, so adding an agent is one class and one
/// registration. A host asks <see cref="Describe"/> for what the agent can do and never branches on its id.
/// <para><b>An implementer must refuse, before anything is spawned, an option its door states
/// <see cref="AgentSupport.Unsupported"/></b> — one terminal <see cref="SessionEnded"/> with
/// <see cref="ProviderVerdict.Unsupported"/>, never an option dropped. <see cref="AgentProvider"/> does that for
/// any door session handed to it.</para></summary>
public interface IAgentProvider
{
    /// <summary>The provider's doors and the abilities each states.</summary>
    AgentProviderDescriptor Describe();

    /// <summary>The session for one door.</summary>
    /// <param name="doorId">An <see cref="AgentDoor.Id"/> from <see cref="Describe"/>.</param>
    /// <exception cref="KeyNotFoundException">The provider has no such door; the message names the doors it has.</exception>
    IAgentSession Open(string doorId);
}

/// <summary>An <see cref="IAgentProvider"/> over a descriptor and the session of each door, with the one rule
/// every provider keeps: an option a door states <see cref="AgentSupport.Unsupported"/> is refused before the
/// door's session is reached. A host registering its own agent builds this rather than the guard itself.
/// <para>The refusal is a single <see cref="SessionEnded"/> with <see cref="ProviderVerdict.Unsupported"/>,
/// <see cref="OptionRefusedSubtype"/> and a diagnostic naming each ability; its failure is
/// <see cref="AgentFailureKind.Fatal"/> with <see cref="AgentDelivery.NeverSent"/>. A session obtained any other
/// way (a keyed <see cref="IAgentSession"/>) keeps its own handling of an option it cannot honour.</para></summary>
public sealed class AgentProvider : IAgentProvider
{
    /// <summary>The <see cref="SessionEnded.Subtype"/> of a turn refused because the door cannot honour an
    /// option.</summary>
    public const string OptionRefusedSubtype = "option-unsupported";

    /// <summary>The <see cref="SessionEnded.Subtype"/> of a turn refused because its
    /// <see cref="AgentSessionOptions.Account"/> belongs to another provider (neither this one's id nor its
    /// <see cref="AgentProviderDescriptor.Agent"/>). Refused the same way as an
    /// unsupported option: one terminal, <see cref="AgentDelivery.NeverSent"/>, nothing spawned.</summary>
    public const string AccountRefusedSubtype = "account-other-provider";

    private readonly AgentProviderDescriptor _descriptor;
    private readonly Dictionary<string, AgentDoorSession> _sessions;

    /// <param name="descriptor">What the provider is. Must have an id, at least one door, and per door a unique
    /// id and at most one claim per ability; an <see cref="AgentProviderDescriptor.Agent"/> needs
    /// <see cref="AgentProviderDescriptor.Accounts"/>.</param>
    /// <param name="sessions">The session of a door, asked for once per door, lazily.</param>
    /// <exception cref="ArgumentException">The descriptor is malformed.</exception>
    public AgentProvider(AgentProviderDescriptor descriptor, Func<AgentDoor, IAgentSession> sessions)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Id, nameof(descriptor));
        if (descriptor.Doors is null || descriptor.Doors.Count == 0)
            throw new ArgumentException($"Agent provider '{descriptor.Id}' states no door.", nameof(descriptor));

        if (descriptor.Agent is not null && string.IsNullOrWhiteSpace(descriptor.Agent))
            throw new ArgumentException($"Agent provider '{descriptor.Id}' names a blank agent.", nameof(descriptor));
        if (descriptor.Agent is not null && descriptor.Accounts is null)
            throw new ArgumentException(
                $"Agent provider '{descriptor.Id}' names agent '{descriptor.Agent}' but reads no account.", nameof(descriptor));

        _descriptor = descriptor;
        _sessions = new Dictionary<string, AgentDoorSession>(StringComparer.Ordinal);
        foreach (var door in descriptor.Doors)
        {
            if (string.IsNullOrWhiteSpace(door.Id))
                throw new ArgumentException($"Agent provider '{descriptor.Id}' has a door with no id.", nameof(descriptor));
            var duplicate = door.Abilities.GroupBy(a => a.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate is not null)
                throw new ArgumentException(
                    $"Door '{door.Id}' of agent provider '{descriptor.Id}' states '{duplicate.Key}' twice.", nameof(descriptor));
            if (!_sessions.TryAdd(door.Id, new AgentDoorSession(descriptor.Id, descriptor.Agent, door, sessions)))
                throw new ArgumentException(
                    $"Agent provider '{descriptor.Id}' has two doors named '{door.Id}'.", nameof(descriptor));
        }
    }

    /// <inheritdoc />
    public AgentProviderDescriptor Describe() => _descriptor;

    /// <inheritdoc />
    public IAgentSession Open(string doorId)
    {
        ArgumentNullException.ThrowIfNull(doorId);
        return _sessions.TryGetValue(doorId, out var session)
            ? session
            : throw new KeyNotFoundException(
                $"Agent provider '{_descriptor.Id}' has no door '{doorId}'. Doors: {string.Join(", ", _sessions.Keys)}.");
    }

    private sealed class AgentDoorSession(
        string providerId, string? agent, AgentDoor door, Func<AgentDoor, IAgentSession> open) : IAgentSession
    {
        private readonly Lazy<IAgentSession> _inner = new(() => open(door));

        public async IAsyncEnumerable<AgentStreamEvent> StreamAsync(
            AgentSessionOptions options, [EnumeratorCancellation] CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            if (options.Account is { } account
                && !string.Equals(account.Provider, providerId, StringComparison.Ordinal)
                && !string.Equals(account.Provider, agent, StringComparison.Ordinal))
            {
                yield return new SessionEnded(ProviderVerdict.Unsupported, true, AccountRefusedSubtype, null, null,
                    $"account '{account.Name}' belongs to '{account.Provider}', not to provider '{providerId}'"
                    + (agent is null ? "" : $" or its agent '{agent}'"))
                {
                    Failure = new AgentFailure(AgentFailureKind.Fatal, "refused-before-spawn") { Delivery = AgentDelivery.NeverSent },
                };
                yield break;
            }

            var refused = door.Unhonourable(options);
            if (refused.Count > 0)
            {
                yield return new SessionEnded(ProviderVerdict.Unsupported, true, OptionRefusedSubtype, null, null,
                    $"door '{door.Id}' cannot honour: {string.Join(", ", refused)}")
                {
                    Failure = new AgentFailure(AgentFailureKind.Fatal, "refused-before-spawn") { Delivery = AgentDelivery.NeverSent },
                };
                yield break;
            }

            await foreach (var evt in _inner.Value.StreamAsync(options, ct).WithCancellation(ct).ConfigureAwait(false))
                yield return evt;
        }
    }
}

/// <summary>The registered <see cref="IAgentProvider"/>s, by id. Where two registrations share an id the later
/// one wins, as the keyed <see cref="IAgentSession"/> beside it does.</summary>
public sealed class AgentProviderRegistry
{
    private readonly Dictionary<string, IAgentProvider> _byId = new(StringComparer.Ordinal);

    /// <param name="providers">Every registered provider.</param>
    /// <exception cref="InvalidOperationException">Providers that share an account key
    /// (<see cref="AgentProviderDescriptor.AccountKey"/>) read the home through different variables, or one names an
    /// agent and reads no account.</exception>
    public AgentProviderRegistry(IEnumerable<IAgentProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        foreach (var provider in providers) _byId[provider.Describe().Id] = provider;
        AgentAccountKeys.Validate(_byId.Values.Select(p => p.Describe()));
        Ids = [.. _byId.Keys.Order(StringComparer.Ordinal)];
    }

    /// <summary>Every registered id, in ordinal order.</summary>
    public IReadOnlyList<string> Ids { get; }

    /// <summary>Every registered provider, in the order of <see cref="Ids"/>.</summary>
    public IReadOnlyList<IAgentProvider> All => [.. Ids.Select(id => _byId[id])];

    /// <summary>The provider registered under <paramref name="id"/>.</summary>
    /// <exception cref="KeyNotFoundException">No provider has that id; the message lists the ones that exist.</exception>
    public IAgentProvider Get(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.TryGetValue(id, out var provider)
            ? provider
            : throw new KeyNotFoundException(
                Ids.Count == 0
                    ? $"No agent provider is registered under '{id}'. None is registered."
                    : $"No agent provider is registered under '{id}'. Registered: {string.Join(", ", Ids)}.");
    }

    /// <summary>Look one up without throwing.</summary>
    public bool TryGet(string id, out IAgentProvider provider)
    {
        ArgumentNullException.ThrowIfNull(id);
        return _byId.TryGetValue(id, out provider!);
    }
}
