using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory;

/// <summary>
/// A blend of member engines, which is itself an engine — so naming, blending, remembering and expanding
/// stay ONE concept rather than four. Members carry hierarchical names ("project/graph"), and every
/// <see cref="MemoryRef"/> records its owning member, which is what makes routing unambiguous.
/// <para><b>It never guesses about capabilities.</b> It always implements <see cref="IExpandableMemory"/>,
/// <see cref="ILinkableMemory"/> and <see cref="IForgettableMemory"/>, and routes strictly by
/// <see cref="MemoryRef.Engine"/>. A wrapper implementing only the base interface would make a capable
/// member invisible — the exact regression that shipped once in the generation router, where wrapping a
/// provider silently stopped every queue-backed render from routing while inline renders kept working and
/// every inline test stayed green.
/// <br/><b>That paragraph was true of two capabilities and false of the third until 3.0</b>, which is why it
/// now names all three. <see cref="IForgettableMemory"/> was omitted, so <c>engine is IForgettableMemory</c>
/// was FALSE for every <c>AddMemoryEngine</c> registration — this class is what
/// <c>MemoryEngineBuilder.Build</c> returns for ALL of them — and a consumer of the shipped memory subsystem
/// had no supported way to delete anything. A comment asserting an invariant is not the invariant.</para>
///
/// <para><b>Reaping fans OUT; expansion and linking ROUTE.</b> The difference is the argument:
/// <see cref="ExpandAsync"/> and <see cref="LinkAsync"/> take a <see cref="MemoryRef"/>, which names exactly
/// one owning member, while reaping takes a (task, scope) that every member may hold material under. Reaping
/// one member and reporting success would leave the rest of the blend holding the very data the caller asked
/// to remove.</para>
/// </summary>
public sealed class CompositeMemoryEngine
    : IMemoryEngine, IExpandableMemory, ILinkableMemory, IForgettableMemory, IPrunableMemory
{
    private readonly IReadOnlyList<IMemoryEngine> _members;
    private readonly ILogger _logger;
    private readonly IMemoryReapPolicy _reapPolicy;

    /// <summary>Compose <paramref name="members"/> under <paramref name="name"/>. Member order is the
    /// render order; authoritative material renders first regardless of position.</summary>
    /// <param name="name">The blend's name.</param>
    /// <param name="members">Its members, each already carrying its own hierarchical name.</param>
    /// <param name="logger">Optional; a member's recall failure is logged rather than thrown.</param>
    /// <param name="reapPolicy">Which members a reap visits. Defaults to <see cref="DefaultMemoryReapPolicy"/>,
    /// which puts an authoritative-ONLY member out of scope — see that type for why eligibility is a
    /// deployment question rather than a property of an engine.</param>
    /// <exception cref="InvalidOperationException">Two members share a name, which would make an entry's
    /// reference ambiguous about its owner.</exception>
    public CompositeMemoryEngine(string name, IReadOnlyList<IMemoryEngine> members,
        ILogger<CompositeMemoryEngine>? logger = null,
        IMemoryReapPolicy? reapPolicy = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(members);
        Name = name;
        _members = members;
        _logger = logger ?? NullLogger<CompositeMemoryEngine>.Instance;
        _reapPolicy = reapPolicy ?? new DefaultMemoryReapPolicy();

        var duplicate = members
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Memory engine '{name}' has two members named '{duplicate.Key}'. Give one an explicit " +
                "label so every entry's reference names exactly one owner.");
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <summary>The members, in render order. Exposed so <see cref="MemoryEngineFactory"/> can index them
    /// alongside the blend — a member is individually addressable, and building it twice to achieve that
    /// would mint a second instance over the same store.</summary>
    public IReadOnlyList<IMemoryEngine> Members => _members;

    /// <inheritdoc />
    public MemoryGrades Supported => _members.Aggregate(MemoryGrades.None, (acc, m) => acc | m.Supported);

    /// <inheritdoc />
    public async Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(write);

        var wanted = write.Grade switch
        {
            MemoryGrade.Authoritative => MemoryGrades.Authoritative,
            MemoryGrade.Associative => MemoryGrades.Associative,
            _ => MemoryGrades.None,
        };

        // Inherit takes the first member; an explicit grade is ROUTED to a member that can hold it.
        // Never downgraded — accepting an authoritative write and storing it as associative is precisely
        // the failure the grade split exists to prevent, and it would be undetectable afterwards.
        var target = wanted == MemoryGrades.None
            ? _members.FirstOrDefault()
            : _members.FirstOrDefault(m => m.Supported.HasFlag(wanted));

        if (target is null)
            throw new NotSupportedException(
                $"Memory engine '{Name}' has no member that can store {write.Grade} material. " +
                $"Members considered: {string.Join(", ", _members.Select(m => m.Name))}.");

        return await target.RememberAsync(write, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var items = new List<MemoryItem>();
        var ran = MemorySources.None;

        // THE ABSTENTION SIGNAL, folded rather than dropped. Through 2.5.x this method returned
        // `new MemoryRecall(items, ran)` — the third positional argument defaulted to null — so
        // MemoryRecall.Answered was ALWAYS null on every DI-registered engine, because
        // MemoryEngineBuilder.Build is documented "ALWAYS a composite, even for one member". A judge that
        // ran and abstained was indistinguishable from no judge at all, which is the one distinction the
        // field exists to make.
        //
        // The fold is a three-value lattice, not a boolean: TRUE if any member's judge found an answer,
        // FALSE if at least one judged and none did, and NULL only when nothing judged anywhere. The last
        // clause is what keeps the shipped default (no verifier) from ever synthesising `false` — a
        // consumer abstaining on `false` would otherwise abstain on everything.
        bool? answered = null;

        foreach (var member in _members)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var recall = await member.RecallAsync(query, ct).ConfigureAwait(false);
                items.AddRange(recall.Items);
                ran |= recall.Ran;
                if (recall.Answered is { } judged) answered = (answered ?? false) || judged;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // one broken member must not sink the blend — the others' material is still good
                _logger.LogWarning(ex, "member {Member} of {Engine} failed during recall; continuing",
                    member.Name, Name);
            }
        }

        if (items.Count == 0) return MemoryRecall.Empty;

        // THE CUT. Every member was asked for up to `Limit` items, so a blend of N members could return N x
        // Limit — and `MemoryEngineBuilder` always wraps members in a composite, so that was the shape every
        // multi-member consumer had. Same class as the AuthoritativeReserve incident: a bound configured on
        // one scope (the query) and enforced on another (nothing).
        //
        // ORDERED BY GRADE FIRST, and that is not a taste. Design §5.7.0 states a LEXICOGRAPHIC objective
        // whose objective (1) — never lose an authoritative fact — is the only one with no acceptable failure
        // rate, so an exact fact must survive a cut that a higher-scoring associative hit would otherwise win.
        // The graph engine already reconciles its own AuthoritativeReserve against a limit the same way.
        //
        // Relevance breaks ties BELOW that tier, and its honest limit is worth stating: two members score on
        // scales that are not comparable (a curated store's relevance and a graph engine's fused rank mean
        // different things), so this ordering is principled about the grade tier and merely reasonable within
        // it. Making the sub-authoritative order better needs a cross-engine measurement this repository has
        // no instrument for — and building a bad one would produce numbers nobody can act on, which is the
        // failure §5.7.0 was written after.
        // Only an EXPLICIT limit cuts. `Limit: null` documents "the engine's default", and a composite has no
        // default of its own — each member already applied its own — so the honest answer is what the members
        // returned. Inventing a constant here would silently truncate a blend somebody deliberately
        // configured, which is a behaviour change wearing a bug fix's clothes; what a composite's own default
        // should be is a real design question and does not get settled by a magic number in this method.
        if (query.Limit is { } limit && limit > 0 && items.Count > limit)
            items = [.. items
                .OrderByDescending(i => i.Grade == MemoryGrade.Authoritative)
                .ThenByDescending(i => i.Relevance)
                .Take(limit)];

        // THE SECOND CUT, and it is the same defect as the first one field over. `CharBudget` travelled to
        // every member unchanged and was reconciled by NOTHING, so an N-member blend could spend N x the
        // budget a caller set — and a caller sets it precisely because it is a prompt budget. Same class as
        // the AuthoritativeReserve incident pitfalls.md records: a bound configured on one scope (the query)
        // and enforced on another (none).
        //
        // The rule is GraphMemoryEngine's own, deliberately identical so a blend and a bare engine cannot
        // answer the same query differently: applied AFTER the limit so the budget cuts the weakest tail
        // rather than changing what wins, an authoritative item is never dropped by it (objective (1) has no
        // acceptable failure rate), and a budget too small even for one item still yields one — a caller
        // asking for less than one fact gets one fact, not nothing.
        if (query.CharBudget is { } budget && budget > 0)
        {
            var spent = 0;
            var kept = new List<MemoryItem>(items.Count);
            foreach (var item in items)
            {
                var cost = item.Content?.Length ?? item.Headline.Length;
                if (item.Grade != MemoryGrade.Authoritative && kept.Count > 0 && spent + cost > budget) continue;
                spent += cost;
                kept.Add(item);
            }
            items = kept;
        }

        return new MemoryRecall(items, ran, answered);
    }

    /// <inheritdoc />
    public async Task<MemoryRecall> ExpandAsync(MemoryRef reference, int hops = 1, int? charBudget = null,
        CancellationToken ct = default)
    {
        var owner = Owner(reference);
        // fail OPEN: a member that cannot expand yields no neighbours rather than an error, because every
        // recall-shaped read in this library degrades rather than throwing
        return owner is IExpandableMemory expandable
            ? await expandable.ExpandAsync(reference, hops, charBudget, ct).ConfigureAwait(false)
            : MemoryRecall.Empty;
    }

    /// <inheritdoc />
    public async Task LinkAsync(MemoryRef from, MemoryRef to, string? kind = null, double weight = 1.0,
        bool symmetric = false, CancellationToken ct = default)
    {
        // fail LOUD: losing a write silently is worse than a throw the caller can see
        if (Owner(from) is not ILinkableMemory linkable)
            throw new NotSupportedException(
                $"Memory engine '{from.Engine}' does not support linking, so the link from '{from.Id}' was " +
                "not recorded.");
        await linkable.LinkAsync(from, to, kind, weight, symmetric, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>Fans out to every member that can reap and SUMS what they removed. Fails LOUD when no member
    /// can — like <see cref="LinkAsync"/> and unlike <see cref="ExpandAsync"/> — because this method returns a
    /// COUNT and <c>0</c> already means "nothing matched". Reporting <c>0</c> for "nothing here can ever
    /// reap" would make a deletion that cannot happen indistinguishable from one that found nothing to do,
    /// and a caller reaping for a consent withdrawal would read it as done.</remarks>
    public async Task<int> PruneAsync(string taskKey, string? scope = null, double? minRetrievability = null,
        TimeSpan? olderThan = null, CancellationToken ct = default)
    {
        var members = ReapableMembers<IPrunableMemory>(nameof(PruneAsync), MemoryReapKind.Prune);

        var reaped = 0;
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            reaped += await member
                .PruneAsync(taskKey, scope, minRetrievability, olderThan, ct).ConfigureAwait(false);
        }
        return reaped;
    }

    /// <inheritdoc />
    /// <remarks>Fans out to every member that can reap, for the reason given on <see cref="PruneAsync"/>:
    /// a (task, scope) is not owned by one member the way a <see cref="MemoryRef"/> is, so forgetting one
    /// member's copy and returning would leave the blend still holding the data.</remarks>
    public async Task ForgetAsync(string taskKey, string? scope = null, CancellationToken ct = default)
    {
        var members = ReapableMembers<IForgettableMemory>(nameof(ForgetAsync), MemoryReapKind.Forget);

        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            await member.ForgetAsync(taskKey, scope, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Refuse BEFORE removing anything unless EVERY member can reap.
    ///
    /// <para><b>Why all-or-nothing, and why the check runs first.</b> The first version of this fanned out to
    /// whichever members implemented <see cref="IForgettableMemory"/> and silently skipped the rest — which
    /// is precisely the outcome the fan-out exists to prevent, written one paragraph above it: reaping some
    /// members and returning success leaves the blend holding the very data the caller asked to remove.
    /// Today only <c>GraphMemoryEngine</c> implements the interface, so
    /// <c>UseCurated("glossary").UseGraph()</c> — a blend from this library's own README — cleared the graph
    /// half, kept the authoritative half, and returned normally. For the call an application makes when a
    /// user withdraws consent, a partial success that reports nothing is the worst available answer.</para>
    ///
    /// <para>Checking first is the other half: a mid-fan-out refusal would delete from the members reached
    /// so far and then throw, which is a partial reap AND an exception. Nothing is removed unless everything
    /// can be.</para>
    ///
    /// <para><b>This takes nothing away.</b> Through 2.5.x <see cref="IForgettableMemory"/> was unreachable
    /// through a composite at all, so a graph-only blend gains a working reap and a mixed blend gets a loud
    /// refusal naming the members that cannot serve it — where before it had no reap to call. Making the
    /// remaining engines forgettable (their stores can: <c>IMemoryStore.ForgetAsync</c>,
    /// <c>ISemanticMemory.ForgetAsync</c>, <c>ICuratedMemoryStore.RemoveAsync</c>) is real, bounded work and
    /// is in the backlog; it is not something to infer half-done from a silent skip.</para></summary>
    /// <summary>The members a reap visits, checked for the SPECIFIC capability that verb needs — refusing
    /// before anything is removed unless every one of them has it.</summary>
    private List<T> ReapableMembers<T>(string verb, MemoryReapKind kind) where T : class
    {
        // A member the POLICY excludes is OUT OF SCOPE, not a gap. It is skipped, and the skip is LOGGED
        // rather than silent — the caller of a consent withdrawal is entitled to know a glossary was kept.
        // The distinction from an incapable member is the whole point: one engine cannot do what was asked,
        // the other was never asked. Eligibility is the deployment's call (IMemoryReapPolicy); capability is
        // the store's, and no policy may configure a genuine gap away.
        var exempt = _members.Where(m => !_reapPolicy.Includes(m, kind)).Select(m => m.Name).ToList();
        if (exempt.Count > 0)
            _logger.LogInformation(
                "memory {Engine}: {Verb} skipped {Count} member(s) the reap policy excludes — {Members}",
                Name, verb, exempt.Count, string.Join(", ", exempt));

        var mine = _members.Where(m => _reapPolicy.Includes(m, kind)).ToList();
        var incapable = mine.Where(m => m is not T).Select(m => m.Name).ToList();
        if (incapable.Count > 0)
            throw new NotSupportedException(
                $"Memory engine '{Name}' cannot {verb}: {incapable.Count} of its {_members.Count} member(s) " +
                $"hold user content and do not implement {typeof(T).Name} — {string.Join(", ", incapable)}. " +
                "Nothing was removed, deliberately: reaping only the capable members would leave the rest of " +
                "the blend holding the data you asked to remove, and report success. Reap those members " +
                "through their own stores, or compose an engine whose members can all serve this verb. " +
                "(A member the reap POLICY excludes is skipped instead of blocking the verb — eligibility is " +
                "IMemoryReapPolicy's question, capability is this interface's.)");

        return [.. mine.Cast<T>()];
    }

    private IMemoryEngine Owner(MemoryRef reference)
    {
        foreach (var member in _members)
            if (string.Equals(member.Name, reference.Engine, StringComparison.Ordinal))
                return member;

        throw new KeyNotFoundException(
            $"No member of memory engine '{Name}' is named '{reference.Engine}'. " +
            $"Members: {string.Join(", ", _members.Select(m => m.Name))}.");
    }
}
