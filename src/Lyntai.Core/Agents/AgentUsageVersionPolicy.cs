namespace Lyntai.Agents;

/// <summary>The host's rule for a binary whose version the library has not validated: whether the version is read at all,
/// and whether the vendor's server is started on what was read. Set on <see cref="AgentUsageReadingOptions.VersionPolicy"/>.
/// <para>A policy widens what THIS host's reads attempt, <b>never what the library claims</b>: the descriptor's
/// <see cref="AgentAbilities.UsageReading"/> claim and its validated versions are the same under every policy, and an answer
/// the reader does not recognise is <see cref="AgentAllowanceUnknown.Unrecognised"/> under every policy.</para>
/// <para><b>For an implementer</b>: both members are called on the read's path, once per read and in this order, and a member
/// that throws refuses the read as <see cref="AgentAllowanceUnknown.VersionNotValidated"/> (the exception's type is logged,
/// nothing is rethrown). A version that cannot be read is <see cref="AgentAllowanceUnknown.VersionUnreadable"/> and the
/// policy is not asked.</para></summary>
public interface IAgentUsageVersionPolicy
{
    /// <summary>Whether the binary is asked its version before <see cref="ShouldStart"/>. False starts no probe, and the
    /// query then carries a null <see cref="AgentUsageVersionQuery.Version"/>.</summary>
    bool ProbesVersion { get; }

    /// <summary>Whether the vendor's server is started for this read. False reads
    /// <see cref="AgentAllowanceUnknown.VersionNotValidated"/> and starts nothing.</summary>
    bool ShouldStart(AgentUsageVersionQuery query);
}

/// <summary>What a policy is asked: the facts of one read, all the library knows about the binary beforehand.</summary>
/// <param name="Provider">The id of the door being read through.</param>
/// <param name="Account">The account being read.</param>
/// <param name="Version">The version the binary printed, as the library parses it (<c>"2.1.296"</c>); null when the policy
/// does not probe.</param>
/// <param name="Validated">Whether the library has validated this version's usage response; false when
/// <paramref name="Version"/> is null.</param>
public sealed record AgentUsageVersionQuery(string Provider, AgentAccount Account, string? Version, bool Validated);

/// <summary>The policies the library ships. A host's own implements <see cref="IAgentUsageVersionPolicy"/>.</summary>
public static class AgentUsageVersionPolicies
{
    /// <summary>The default: read the version, and start the server only for a version the library has validated.</summary>
    public static IAgentUsageVersionPolicy ValidatedOnly { get; } = new ValidatedOnlyPolicy();

    /// <summary>Start the server on whatever version is installed, with no version probe. A read under it is
    /// <b>only as good as the installed binary's answer</b>: the reading's <see cref="AgentAllowanceReading.Version"/> is null.</summary>
    public static IAgentUsageVersionPolicy AnyVersion { get; } = new AnyVersionPolicy();

    private sealed class ValidatedOnlyPolicy : IAgentUsageVersionPolicy
    {
        public bool ProbesVersion => true;
        public bool ShouldStart(AgentUsageVersionQuery query) => query.Validated;
    }

    private sealed class AnyVersionPolicy : IAgentUsageVersionPolicy
    {
        public bool ProbesVersion => false;
        public bool ShouldStart(AgentUsageVersionQuery query) => true;
    }
}
