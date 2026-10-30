namespace Lyntai.Memory;

/// <summary>What a proposed change to what memory treats as current would do.</summary>
public enum MemoryChangeKind
{
    /// <summary>A new claim replaces named entries: they end, and it succeeds them.</summary>
    Supersede,
    /// <summary>A named entry stops being current with no successor.</summary>
    End,
}

/// <summary>One entry a proposal would end, and the revision its proposer read.</summary>
/// <param name="Reference">The entry, exactly as the engine issued it.</param>
/// <param name="Revision">The <see cref="MemoryItem.Revision"/> the proposer read; an approval is bound to it, and the end
/// applies only if the entry is still at it.</param>
public sealed record MemoryChangeTarget(MemoryRef Reference, string Revision);

/// <summary>A proposal put to the <see cref="IMemoryChangeApprover"/>s: the exact references, the claim and the evidence.
/// <para>A model's proposal is not a confirmation (<c>docs/DECISIONS.md</c> D109, D217): the approver is the host's person,
/// registered deterministic check, or a model past a bound the host measured.</para></summary>
/// <param name="Kind">What would change.</param>
/// <param name="TaskKey">The task the entries belong to.</param>
/// <param name="Targets">The entries that would end, each with the revision read.</param>
/// <param name="Claim">The successor's content for <see cref="MemoryChangeKind.Supersede"/>; null for an end.</param>
/// <param name="Reason">Why, as the proposer stated it.</param>
/// <param name="Evidence">What the proposer offered for it, or null.</param>
public sealed record MemoryChangeRequest(MemoryChangeKind Kind, string TaskKey, IReadOnlyList<MemoryChangeTarget> Targets,
    string? Claim, string Reason, string? Evidence);

/// <summary>An <see cref="IMemoryChangeApprover"/>'s answer: <see cref="Approve"/>, or <see cref="Deny"/> with the reason the
/// proposer is told.</summary>
public sealed record MemoryChangeDecision
{
    private MemoryChangeDecision(bool approved, string? reason)
    {
        Approved = approved;
        Reason = reason;
    }

    /// <summary>Whether the change may be applied.</summary>
    public bool Approved { get; }

    /// <summary>On a refusal, why — read by the proposer, so write it to be acted on.</summary>
    public string? Reason { get; }

    /// <summary>Let the change be applied, to the revisions the request named.</summary>
    public static MemoryChangeDecision Approve { get; } = new(true, null);

    /// <summary>Refuse the change, telling the proposer <paramref name="reason"/>.</summary>
    public static MemoryChangeDecision Deny(string reason) => new(false, reason);
}

/// <summary>Approves or refuses ONE proposed supersession or end before <see cref="MemoryChangeGate"/> applies it. Registered
/// as a DI collection (<c>AddMemoryChangeApprover</c>), in the shape of <see cref="Lyntai.Agents.IToolApprover"/>.
/// <para><b>The library ships no approver that approves by itself</b> (D241): a host that wants an agent to apply alone writes
/// that approver, and so owns the decision (D218). Every registered approver must approve, asked in registration order, and
/// the first refusal decides; one that throws, or answers null, refuses — the gate fails closed.</para></summary>
public interface IMemoryChangeApprover
{
    /// <summary>Decide one proposal.</summary>
    /// <param name="request">The proposal.</param>
    /// <param name="ct">The caller's cancellation.</param>
    Task<MemoryChangeDecision> ApproveAsync(MemoryChangeRequest request, CancellationToken ct = default);
}

/// <summary>What <see cref="MemoryChangeGate"/> did with a proposal.</summary>
public enum MemoryChangeStatus
{
    /// <summary>Approved and applied.</summary>
    Applied,
    /// <summary>An approver refused; nothing ended.</summary>
    Denied,
    /// <summary>No approver is registered, so nothing ended.</summary>
    NoApprover,
    /// <summary>Approved, but an entry is no longer at the revision the proposal read; nothing ended, and a fresh approval is needed.</summary>
    Changed,
    /// <summary>An end was already recorded for an entry; nothing more was written for it.</summary>
    AlreadyEnded,
    /// <summary>A reference was not one this engine issued for this task, or the engine cannot end an entry; nothing ended.</summary>
    Refused,
}

/// <summary>The outcome of one proposal, written to be read by the proposer.</summary>
/// <param name="Status">What happened.</param>
/// <param name="Message">Why, in words a model can act on.</param>
/// <param name="Successor">For a supersession, the stored successor — present even when the end did not apply, so a retry
/// finds the same entry; null for an end.</param>
/// <param name="Revisions">Per target that did not end for a revision mismatch, the revision found: what a fresh review reads.</param>
public sealed record MemoryChangeResult(MemoryChangeStatus Status, string Message, MemoryRef? Successor = null,
    IReadOnlyDictionary<MemoryRef, string>? Revisions = null);
