namespace Lyntai.Providers.Acp;

/// <summary>Answers the permission request an ACP agent makes before it runs a tool (<c>session/request_permission</c>),
/// so an application that lets a person decide can hand the question to them. Passed as <c>permissionApprover:</c> to
/// <c>AddAcpAgentSession</c>; none registered leaves every request to the turn's
/// <see cref="Lyntai.Agents.AgentSessionOptions.ToolPolicy"/>, and the door states
/// <see cref="Lyntai.Agents.AgentAbilities.PermissionRequests"/> <see cref="Lyntai.Agents.AgentSupport.Unsupported"/>.
/// <para><b>Only a write turn asks.</b> A read-only turn rejects every request without consulting the approver, so
/// the read-only gate stays a promise the approver cannot weaken.</para>
/// <para><b>The answer is written once.</b> The agent is answered when the approver answers, when the turn is
/// cancelled (the protocol's <c>cancelled</c> outcome), or, by default, when half of the turn's inactivity window has passed
/// with no answer, which is a refusal: the other half is the agent's to act on it before the clock a silent
/// agent does not reset ends the process. A host that registers <see cref="AcpPermissionWait.AsLongAsApproverHolds"/> has no
/// such window: the request waits as long as the approver holds it. An approver that throws, answers null or names an option the agent did
/// not offer refuses, so the gate fails closed. The token it is handed is cancelled when any of these happens, and
/// an answer that arrives after is dropped.</para>
/// <para>Requests of one turn may be held at once, so an implementation is thread-safe.</para></summary>
public interface IAcpPermissionApprover
{
    /// <summary>Decide one request.</summary>
    /// <param name="request">What the agent asked.</param>
    /// <param name="ct">Cancelled when the request stops waiting for an answer: the window passed, the turn was
    /// cancelled or the turn ended.</param>
    Task<AcpPermissionAnswer> ApproveAsync(AcpPermissionRequest request, CancellationToken ct = default);
}

/// <summary>How long a permission request waits for its <see cref="IAcpPermissionApprover"/>.</summary>
public enum AcpPermissionWait
{
    /// <summary>Half of the turn's inactivity window, then a refusal. The default: a host that registers an approver and
    /// forgets it never hangs a turn.</summary>
    HalfTheWindow = 0,

    /// <summary>As long as the approver holds the request: the wait is the approver's, a person's, and the process runner is told
    /// not to read it as a silent agent, so the inactivity window starts again from the answer. The turn is still ended by its
    /// caller's cancellation (the protocol's <c>cancelled</c> outcome). It needs a runner that can be told
    /// (<see cref="Lyntai.Processes.IProcessRunner.CanPauseInactivity"/>), and a registration whose runner cannot is
    /// refused.</summary>
    AsLongAsApproverHolds,
}

/// <summary>One permission request put to the <see cref="IAcpPermissionApprover"/>.</summary>
/// <param name="SessionId">The agent's session the request belongs to.</param>
/// <param name="ToolCallJson">The request's <c>toolCall</c> object as the agent wrote it: its title, kind, locations
/// and raw input, whatever the agent said of the tool. <c>{}</c> when it said none.</param>
/// <param name="Options">The choices the agent offered, in its order.</param>
public sealed record AcpPermissionRequest(string SessionId, string ToolCallJson, IReadOnlyList<AcpPermissionOption> Options);

/// <summary>One choice an agent offers with a permission request.</summary>
/// <param name="OptionId">The id the answer names, <see cref="AcpPermissionAnswer.Choose"/>.</param>
/// <param name="Name">What the agent calls it, for a person to read; empty when it named none.</param>
/// <param name="Kind">The protocol's kind: <c>allow_once</c>, <c>allow_always</c>, <c>reject_once</c> or
/// <c>reject_always</c>; empty when the agent stated none, and a word the protocol may add later is passed on as it is.</param>
public sealed record AcpPermissionOption(string OptionId, string Name, string Kind);

/// <summary>An <see cref="IAcpPermissionApprover"/>'s answer: <see cref="Approve"/> or <see cref="Deny"/>, which the
/// door maps onto the options the agent offered, or <see cref="Choose"/> for one of them by id.</summary>
public sealed record AcpPermissionAnswer
{
    private AcpPermissionAnswer(bool approves, string? optionId)
    {
        Approves = approves;
        OptionId = optionId;
    }

    /// <summary>Whether this answer allows. False for <see cref="Deny"/>; for <see cref="Choose"/> it is not what
    /// the choice does, which is the option's own kind.</summary>
    public bool Approves { get; }

    /// <summary>The option <see cref="Choose"/> names, or null.</summary>
    public string? OptionId { get; }

    /// <summary>Allow this one call: the option of kind <c>allow_once</c>. Never an <c>allow_always</c>, which
    /// grants the agent more than was asked, so an agent that offers no <c>allow_once</c> is answered
    /// <c>cancelled</c>; naming an <c>allow_always</c> is <see cref="Choose"/>'s.</summary>
    public static AcpPermissionAnswer Approve { get; } = new(true, null);

    /// <summary>Refuse this one call: the option of kind <c>reject_once</c>, else <c>reject_always</c>, else
    /// <c>cancelled</c> for an agent that offers neither.</summary>
    public static AcpPermissionAnswer Deny { get; } = new(false, null);

    /// <summary>Answer with one of the agent's own options, such as an <c>allow_always</c> a person chose. An id the
    /// agent did not offer refuses.</summary>
    /// <param name="optionId">An <see cref="AcpPermissionOption.OptionId"/> of the request.</param>
    public static AcpPermissionAnswer Choose(string optionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionId);
        return new AcpPermissionAnswer(false, optionId);
    }
}
