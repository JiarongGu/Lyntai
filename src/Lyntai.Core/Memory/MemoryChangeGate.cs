using System.Globalization;

namespace Lyntai.Memory;

/// <summary>Puts a proposed supersession or end to the registered <see cref="IMemoryChangeApprover"/>s and applies it only
/// if they all approve, bound to the revision the proposer read (<c>docs/DECISIONS.md</c> D241). It is what an agent's
/// <c>supersedes</c> and end tools call.
/// <para><b>It fails closed, and it never ends anything on its own say.</b> With no approver registered nothing ends; a
/// supersession's successor is still stored, live and unlinked, because an observation is not a confirmation. An approver
/// that throws or answers null refuses. An approval applies through <see cref="IEndableMemory.EndAsync"/> with the revision
/// the proposal named, so an entry rewritten after it was read changes nothing and needs a fresh approval; the guard is
/// never dropped to repair a partial outcome.</para>
/// <para><b>An approver is shown what would apply</b> (<see cref="MemoryChangeRequest.Successor"/>, <see cref="MemoryChangeRequest.At"/>,
/// <see cref="MemoryChangeRequest.Write"/>), and never asked about what could not: a successor whose content is an entry that has
/// ended, or an instant before a target began, is <see cref="MemoryChangeStatus.Refused"/> first. Those two are read through
/// <see cref="IViewableMemory"/>, which moves nothing; an engine without a view is left to its own <see cref="IEndableMemory.EndAsync"/>
/// after approval.</para></summary>
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
    /// <param name="by">The entry of this engine that replaced it, or null for an end that names none.</param>
    /// <param name="at">When it stopped holding; null is now, or the successor's start. One before the entry began is refused before
    /// any approver is asked.</param>
    /// <param name="ct">Cancellation.</param>
    public Task<MemoryChangeResult> EndAsync(IMemoryEngine engine, string taskKey, MemoryRef entry, string revision,
        string reason, string? evidence = null, MemoryRef? by = null, DateTimeOffset? at = null,
        CancellationToken ct = default) =>
        RunAsync(engine, MemoryChangeKind.End, taskKey, null, [new MemoryChangeTarget(entry, revision)], reason, evidence,
            by, at, ct);

    /// <summary>Propose that <paramref name="write"/> replaces <paramref name="targets"/>. The write is stored live and
    /// unlinked first, so its identity is the same on a retry; the targets end only on approval. A write whose content is an entry
    /// that has ended lands on that entry, which cannot replace anything: refused before any approver is asked.</summary>
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
        return RunAsync(engine, MemoryChangeKind.Supersede, write.TaskKey, write, targets, reason, evidence, null, null, ct);
    }

    private async Task<MemoryChangeResult> RunAsync(IMemoryEngine engine, MemoryChangeKind kind, string taskKey,
        MemoryWrite? write, IReadOnlyList<MemoryChangeTarget> targets, string reason, string? evidence, MemoryRef? by,
        DateTimeOffset? at, CancellationToken ct)
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
        MemoryRef? successor = by;
        if (write is not null)
            successor = (await engine.RememberAsync(write, ct).ConfigureAwait(false)).Reference;

        // what the engine would refuse after an approval is refused before one is asked for, so no approver reviews what cannot apply
        if (await RefusalAsync(engine, taskKey, targets, successor, write is null ? null : successor, at, ct).ConfigureAwait(false)
            is { } refused)
            return new MemoryChangeResult(MemoryChangeStatus.Refused, refused, successor);

        var request = new MemoryChangeRequest(kind, taskKey, targets, write?.Content, reason.Trim(), evidence)
        {
            Successor = successor, At = at, Write = write,
        };
        if (_approvers.Count == 0)
            return new MemoryChangeResult(MemoryChangeStatus.NoApprover,
                "No approver is registered, so nothing was ended" + (write is null ? "." : "; the new entry is stored, live, and replaces nothing."),
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
                    + (write is null ? "" : " The new entry is stored, live, and replaces nothing."), successor);
        }

        return await ApplyAsync(engine, taskKey, targets, successor, reason.Trim(), at, ct).ConfigureAwait(false);
    }

    /// <summary>Why the engine would refuse the proposal after an approval, or null: a stored successor that has ended (its content
    /// named an entry an earlier change ended), or an instant before a target began. Read through a view, which moves nothing; an
    /// engine that holds no view is left to its own end after approval.</summary>
    private static async Task<string?> RefusalAsync(IMemoryEngine engine, string taskKey, IReadOnlyList<MemoryChangeTarget> targets,
        MemoryRef? successor, MemoryRef? storedSuccessor, DateTimeOffset? at, CancellationToken ct)
    {
        if (engine is not IViewableMemory { HoldsView: true } viewable) return null;
        var view = viewable.ViewOf(taskKey);
        var next = successor is { } named ? await ReadAsync(view, named, ct).ConfigureAwait(false) : null;
        // ended by now, or by its own start (which the stores judge); one that starts later is a scheduled successor and passes
        if (storedSuccessor is { } stored && next?.Item.Validity is { } validity
            && (next.Standing == MemoryStanding.Ended || validity.HasEndedBy(validity.From ?? DateTimeOffset.MinValue)))
            return $"The new claim is entry '{stored.Id}', already held, and it has ended" +
                (validity.EndedReason is { } why ? $" (\"{why}\")" : "") + ", so it cannot replace anything. Nothing was ended and no " +
                "approver was asked. An ended entry stays ended: state the claim in other words.";

        // the instant the engine would end each target at: the one stated, else the successor's start, else now
        var instant = at ?? next?.Item.Validity?.From;
        foreach (var target in targets)
        {
            if (await ReadAsync(view, target.Reference, ct).ConfigureAwait(false) is not { } held
                || held.Item.Validity?.From is not { } began) continue;
            if (instant is { } end ? end < began : held.Standing == MemoryStanding.NotYetValid)
                return $"Entry '{target.Reference.Id}' began at {began:O}, after the {(instant is { } i ? i.ToString("O", CultureInfo.InvariantCulture) : "now")} " +
                    "this would end it: an entry cannot end before it started. Nothing was ended and no approver was asked.";
        }
        return null;
    }

    // one entry by reference, whatever its validity, through a view: no reinforcement, no walk
    private static async Task<MemoryViewItem?> ReadAsync(IMemoryView view, MemoryRef reference, CancellationToken ct) =>
        (await view.ExpandAsync(reference, hops: 0, validity: MemoryValidityReading.History, ct: ct).ConfigureAwait(false))
            .Items.FirstOrDefault();

    private static async Task<MemoryChangeResult> ApplyAsync(IMemoryEngine engine, string taskKey,
        IReadOnlyList<MemoryChangeTarget> targets, MemoryRef? successor, string reason, DateTimeOffset? at, CancellationToken ct)
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
                    new MemoryEnding(reason) { Successor = successor, At = at, ExpectedRevision = target.Revision }, ct).ConfigureAwait(false);
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
