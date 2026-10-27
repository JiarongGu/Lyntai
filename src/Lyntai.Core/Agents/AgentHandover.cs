namespace Lyntai.Agents;

/// <summary>What a hand-over does with the conversation.</summary>
public enum AgentHandoverKind
{
    /// <summary>Continue the same conversation with its resume token.</summary>
    Resume,

    /// <summary>Start a fresh session whose prompt carries a brief.</summary>
    FreshWithBrief,
}

/// <summary>Why a hand-over went the way it did: the first check that failed, or <see cref="Allowed"/>.</summary>
public enum AgentHandoverReason
{
    /// <summary>The provider, door and account are the conversation's own and the door states resume supported.</summary>
    Allowed,

    /// <summary>No recorded turn gave a session id, so there is no conversation to pick up.</summary>
    NoConversation,

    /// <summary>The target is another provider; a conversation cannot be copied into it.</summary>
    ProviderChanged,

    /// <summary>The target is another door of the provider; a resume token belongs to the door that issued it.</summary>
    DoorChanged,

    /// <summary>The target is another account; the conversation lives under the one that started it.</summary>
    AccountChanged,

    /// <summary>The target provider has no such door.</summary>
    NoSuchDoor,

    /// <summary>The door states resume <see cref="AgentSupport.Unsupported"/>.</summary>
    ResumeUnsupported,

    /// <summary>The door states resume <see cref="AgentSupport.Unknown"/>: not promised, so not relied on.</summary>
    ResumeNotMeasured,
}

/// <summary>The outcome of <see cref="AgentHandover.Decide"/>.</summary>
/// <param name="Kind">Resume the conversation, or start fresh with a brief.</param>
/// <param name="Reason">Why.</param>
/// <param name="ResumeToken">The token to resume with; null unless <paramref name="Kind"/> is
/// <see cref="AgentHandoverKind.Resume"/>.</param>
public sealed record AgentHandoverDecision(AgentHandoverKind Kind, AgentHandoverReason Reason, string? ResumeToken)
{
    /// <summary>The options for the next turn under this decision. A resume sets <see cref="AgentSessionOptions.ResumeToken"/>
    /// and leaves the prompt alone. A fresh session clears the token and leads the prompt with the brief, then the
    /// host's new request after a marker — where the brief sits in a prompt is a choice this method makes for the
    /// neutral options; an adapter that carries a system prompt elsewhere is still handed it as the prompt.</summary>
    /// <param name="next">The options of the turn about to run; its runtime type is kept.</param>
    /// <param name="brief">The brief to carry; required for a fresh session, ignored for a resume.</param>
    /// <exception cref="ArgumentNullException"><paramref name="brief"/> is null for a fresh session.</exception>
    public AgentSessionOptions Apply(AgentSessionOptions next, AgentBrief? brief)
    {
        ArgumentNullException.ThrowIfNull(next);
        if (Kind == AgentHandoverKind.Resume) return next with { ResumeToken = ResumeToken };
        ArgumentNullException.ThrowIfNull(brief);
        return next with
        {
            ResumeToken = null,
            Prompt = brief.Text + "\nNEW REQUEST FROM THE HOST\n" + next.Prompt,
        };
    }
}

/// <summary>The hand-over decision: whether a conversation can go on where it is or must be handed to a fresh
/// session as a brief. A pure function of the transcript and the target's description — it moves nothing and
/// spawns nothing; <i>when</i> to hand over stays the host's.</summary>
public static class AgentHandover
{
    /// <summary>Resume only when every check holds: a session id was recorded; the target is the same provider,
    /// the same door and the same account (equal strings, or both null); and the door states
    /// <see cref="AgentAbilities.Resume"/> <see cref="AgentSupport.Supported"/>. Anything else — an unknown
    /// ability included, since unknown is not promised — is a fresh session handed the brief.</summary>
    /// <param name="transcript">The conversation so far.</param>
    /// <param name="target">The provider the next turn will run on.</param>
    /// <param name="doorId">The door of <paramref name="target"/> it will run through.</param>
    /// <param name="account">The host's name for the target account; null = the default one.</param>
    public static AgentHandoverDecision Decide(
        AgentTranscript transcript, AgentProviderDescriptor target, string doorId, string? account = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(doorId);

        if (transcript.LatestSession is not { } last) return Fresh(AgentHandoverReason.NoConversation);
        var from = last.Source;
        if (!string.Equals(from.ProviderId, target.Id, StringComparison.Ordinal))
            return Fresh(AgentHandoverReason.ProviderChanged);
        if (!string.Equals(from.DoorId, doorId, StringComparison.Ordinal))
            return Fresh(AgentHandoverReason.DoorChanged);
        if (!string.Equals(from.Account, account, StringComparison.Ordinal))
            return Fresh(AgentHandoverReason.AccountChanged);

        var door = target.Doors.FirstOrDefault(d => string.Equals(d.Id, doorId, StringComparison.Ordinal));
        if (door is null) return Fresh(AgentHandoverReason.NoSuchDoor);
        return door.SupportFor(AgentAbilities.Resume) switch
        {
            AgentSupport.Supported => new AgentHandoverDecision(AgentHandoverKind.Resume, AgentHandoverReason.Allowed, last.SessionId),
            AgentSupport.Unsupported => Fresh(AgentHandoverReason.ResumeUnsupported),
            _ => Fresh(AgentHandoverReason.ResumeNotMeasured),
        };
    }

    private static AgentHandoverDecision Fresh(AgentHandoverReason reason) =>
        new(AgentHandoverKind.FreshWithBrief, reason, null);
}
