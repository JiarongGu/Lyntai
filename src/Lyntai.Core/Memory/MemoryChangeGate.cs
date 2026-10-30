namespace Lyntai.Memory;

/// <summary>Puts a proposed supersession or end to the registered <see cref="IMemoryChangeApprover"/>s and applies it only
/// if they all approve, bound to the revision the proposer read (<c>docs/DECISIONS.md</c> D241). It is what an agent's
/// <c>supersedes</c> and end tools call.
/// <para><b>It fails closed, and it never ends anything on its own say.</b> With no approver registered nothing ends; a
/// supersession's successor is still stored, live and unlinked, because an observation is not a confirmation. An approver
/// that throws or answers null refuses. An approval applies through <see cref="IEndableMemory.EndAsync"/> with the revision
/// the proposal named, so an entry rewritten after it was read changes nothing and needs a fresh approval; the guard is
/// never dropped to repair a partial outcome.</para></summary>
/// <param name="approvers">The registered approvers, in registration order.</param>
public sealed class MemoryChangeGate(IEnumerable<IMemoryChangeApprover> approvers)
{
    private readonly IReadOnlyList<IMemoryChangeApprover> _approvers = [.. approvers];

    /// <summary>Propose that <paramref name="entry"/> stops being current, with no successor.</summary>
    /// <param name="engine">The engine the entry belongs to — a blend or a graph engine.</param>
    /// <param name="taskKey">The task the entry must belong to.</param>
    /// <param name="entry">The entry, as the engine issued it.</param>
    /// <param name="revision">The <see cref="MemoryItem.Revision"/> read; required, since an approval is bound to it.</param>
    /// <param name="reason">Why it ended, as the proposer states it.</param>
    /// <param name="evidence">What the proposer offers for it.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<MemoryChangeResult> EndAsync(IMemoryEngine engine, string taskKey, MemoryRef entry, string revision,
        string reason, string? evidence = null, CancellationToken ct = default) =>
        RunAsync(engine, MemoryChangeKind.End, taskKey, null, [new MemoryChangeTarget(entry, revision)], reason, evidence, ct);

    /// <summary>Propose that <paramref name="write"/> replaces <paramref name="targets"/>. The write is stored live and
    /// unlinked first, so its identity is the same on a retry; the targets end only on approval.</summary>
    /// <param name="engine">The engine the entries belong to.</param>
    /// <param name="write">The successor; its <see cref="MemoryWrite.Supersedes"/> must be empty.</param>
    /// <param name="targets">The entries it replaces, each with the revision read.</param>
    /// <param name="reason">Why, as the proposer states it.</param>
    /// <param name="evidence">What the proposer offers for it.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<MemoryChangeResult> SupersedeAsync(IMemoryEngine engine, MemoryWrite write,
        IReadOnlyList<MemoryChangeTarget> targets, string reason, string? evidence = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);
        if (write.Supersedes is { Count: > 0 })
            throw new ArgumentException("A write put to the gate names its predecessors as targets, not as Supersedes.", nameof(write));
        return RunAsync(engine, MemoryChangeKind.Supersede, write.TaskKey, write, targets, reason, evidence, ct);
    }

    private async Task<MemoryChangeResult> RunAsync(IMemoryEngine engine, MemoryChangeKind kind, string taskKey,
        MemoryWrite? write, IReadOnlyList<MemoryChangeTarget> targets, string reason, string? evidence, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(taskKey);
        ArgumentNullException.ThrowIfNull(targets);
        ct.ThrowIfCancellationRequested();
        if (targets.Count == 0)
            return new MemoryChangeResult(MemoryChangeStatus.Refused, "Name the entry to end.");
        if (string.IsNullOrWhiteSpace(reason))
            return new MemoryChangeResult(MemoryChangeStatus.Refused, "Say why: a reason is required.");
        if (targets.Any(t => string.IsNullOrWhiteSpace(t.Revision)))
            return new MemoryChangeResult(MemoryChangeStatus.Refused,
                "Pass the revision you read for each entry, from the recall that returned it: an approval is bound to it.");

        // the observation is stored whatever the answer: it is the AGENT's, and only its ending of another entry is gated
        MemoryRef? successor = null;
        if (write is not null)
            successor = (await engine.RememberAsync(write, ct).ConfigureAwait(false)).Reference;

        var request = new MemoryChangeRequest(kind, taskKey, targets, write?.Content, reason.Trim(), evidence);
        if (_approvers.Count == 0)
            return new MemoryChangeResult(MemoryChangeStatus.NoApprover,
                "No approver is registered, so nothing was ended" + (successor is null ? "." : "; the new entry is stored, live, and replaces nothing."),
                successor);
        foreach (var approver in _approvers)
        {
            MemoryChangeDecision? decision;
            try
            {
                decision = await approver.ApproveAsync(request, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception)
            {
                decision = null;   // an approver that cannot answer has not approved
            }
            if (decision is not { Approved: true })
                return new MemoryChangeResult(MemoryChangeStatus.Denied,
                    "Not approved: " + (decision?.Reason is { Length: > 0 } why ? why : "the approver gave no answer.")
                    + (successor is null ? "" : " The new entry is stored, live, and replaces nothing."), successor);
        }

        return await ApplyAsync(engine, taskKey, targets, successor, reason.Trim(), ct).ConfigureAwait(false);
    }

    private static async Task<MemoryChangeResult> ApplyAsync(IMemoryEngine engine, string taskKey,
        IReadOnlyList<MemoryChangeTarget> targets, MemoryRef? successor, string reason, CancellationToken ct)
    {
        if (engine is not IEndableMemory endable)
            return new MemoryChangeResult(MemoryChangeStatus.Refused,
                $"Memory '{engine.Name}' cannot end an entry, so nothing was ended.", successor);

        var ended = 0;
        var already = 0;
        var changed = new Dictionary<MemoryRef, string>();
        foreach (var target in targets)
        {
            try
            {
                var result = await endable.EndAsync(taskKey, target.Reference,
                    new MemoryEnding(reason) { Successor = successor, ExpectedRevision = target.Revision }, ct).ConfigureAwait(false);
                switch (result.Outcome)
                {
                    case MemoryEndOutcome.Ended: ended++; break;
                    case MemoryEndOutcome.AlreadyEnded: already++; break;
                    default: changed[target.Reference] = result.Revision ?? ""; break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or NotSupportedException
                                           or KeyNotFoundException)
            {
                // a reference the host did not issue, another task's, or a second successor: said, and nothing more is ended
                return new MemoryChangeResult(MemoryChangeStatus.Refused,
                    $"Refused: {ex.Message}" + (ended > 0 ? $" ({ended} earlier end(s) in this proposal had already applied.)" : ""),
                    successor, changed.Count == 0 ? null : changed);
            }
        }

        if (changed.Count > 0)
            return new MemoryChangeResult(MemoryChangeStatus.Changed,
                $"{changed.Count} entr{(changed.Count == 1 ? "y has" : "ies have")} changed since you read " +
                $"{(changed.Count == 1 ? "it" : "them")}, so {(changed.Count == 1 ? "it was" : "they were")} not ended; read again and propose again." +
                (ended > 0 ? $" {ended} other end(s) applied." : ""), successor, changed);
        return ended == 0 && already > 0
            ? new MemoryChangeResult(MemoryChangeStatus.AlreadyEnded, "Already ended; nothing changed.", successor)
            : new MemoryChangeResult(MemoryChangeStatus.Applied, $"Ended {ended}.", successor);
    }
}
