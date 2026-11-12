using Lyntai.Inference;

namespace Lyntai.Agents;

/// <summary>An optional capability of an <see cref="IAgentProvider"/>, found by type test as <see cref="IAgentUsageSource"/> is:
/// whether an account is signed in, and starting its sign-in, under the account's own configuration home. The door states it as
/// <see cref="AgentAbilities.SignIn"/>. It reuses the inference providers' answer types (<see cref="ProviderAuthStatus"/>,
/// <see cref="ProviderLoginRequest"/>, <see cref="ProviderAuthResult"/>), so a host that reads one reads the other.
/// <para>Neither member runs a model turn, stores a credential or reads a home: the vendor's own program does, as
/// <see cref="IProviderAuth"/> says of the inference doors. Neither throws for a refused account or a failed program: that is a
/// value, and only caller cancellation propagates.</para></summary>
public interface IAgentAuthSource
{
    /// <summary>Report whether <paramref name="account"/> is signed in, and as whom, without a turn. <b>"Not signed in" is an answer</b>
    /// (<see cref="ProviderAuthStatus.Authenticated"/> false), and so is an account the door refuses or a program that cannot be asked,
    /// each with the reason in <see cref="ProviderAuthStatus.Detail"/>.</summary>
    /// <param name="account">The account whose home is read; null reads the registration's own home. An account of another provider,
    /// or one the door cannot map, is refused as an answer before anything is spawned.</param>
    /// <param name="ct">Cancels the ask.</param>
    Task<ProviderAuthStatus> StatusAsync(AgentAccount? account = null, CancellationToken ct = default);

    /// <summary>Start the vendor's own sign-in under <paramref name="account"/>'s home, then report the state it left behind. BLOCKS
    /// until the flow ends or the vendor's budget expires: the flow prints an address or a code and then waits for a person, so
    /// <paramref name="onOutput"/> hands each line the program prints as it prints it, while the call is still waiting.</summary>
    /// <param name="account">As <see cref="StatusAsync"/>.</param>
    /// <param name="request">What the vendor's flow takes, as <see cref="IProviderAuth.LoginAsync"/> says; a value the vendor has no
    /// flag for is refused as a failed result, never invented into one.</param>
    /// <param name="onOutput">Called once per line of the program's output, in order, on the thread that reads it; null reads none. The
    /// library does not read a line: an address, a code or a port in it is the vendor's wording, which the host shows as printed.
    /// A callback that throws ends the flow and is reported as a failed result.</param>
    /// <param name="ct">Cancels the wait and kills the program.</param>
    Task<ProviderAuthResult> LoginAsync(
        AgentAccount? account = null, ProviderLoginRequest? request = null, Action<string>? onOutput = null, CancellationToken ct = default);
}
