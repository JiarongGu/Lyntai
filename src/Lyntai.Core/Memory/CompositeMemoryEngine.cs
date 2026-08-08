using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lyntai.Memory;

/// <summary>
/// A blend of member engines, which is itself an engine — so naming, blending, remembering and expanding
/// stay ONE concept rather than four. Members carry hierarchical names ("project/graph"), and every
/// <see cref="MemoryRef"/> records its owning member, which is what makes routing unambiguous.
/// <para><b>It never guesses about capabilities.</b> It always implements <see cref="IExpandableMemory"/>
/// and <see cref="ILinkableMemory"/> and routes strictly by <see cref="MemoryRef.Engine"/>. A wrapper
/// implementing only the base interface would make a capable member invisible — the exact regression that
/// shipped once in the generation router, where wrapping a provider silently stopped every queue-backed
/// render from routing while inline renders kept working and every inline test stayed green.</para>
/// </summary>
public sealed class CompositeMemoryEngine : IMemoryEngine, IExpandableMemory, ILinkableMemory
{
    private readonly IReadOnlyList<IMemoryEngine> _members;
    private readonly ILogger _logger;

    /// <summary>Compose <paramref name="members"/> under <paramref name="name"/>. Member order is the
    /// render order; authoritative material renders first regardless of position.</summary>
    /// <param name="name">The blend's name.</param>
    /// <param name="members">Its members, each already carrying its own hierarchical name.</param>
    /// <param name="logger">Optional; a member's recall failure is logged rather than thrown.</param>
    /// <exception cref="InvalidOperationException">Two members share a name, which would make an entry's
    /// reference ambiguous about its owner.</exception>
    public CompositeMemoryEngine(string name, IReadOnlyList<IMemoryEngine> members,
        ILogger<CompositeMemoryEngine>? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(members);
        Name = name;
        _members = members;
        _logger = logger ?? NullLogger<CompositeMemoryEngine>.Instance;

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

        foreach (var member in _members)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var recall = await member.RecallAsync(query, ct).ConfigureAwait(false);
                items.AddRange(recall.Items);
                ran |= recall.Ran;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                // one broken member must not sink the blend — the others' material is still good
                _logger.LogWarning(ex, "member {Member} of {Engine} failed during recall; continuing",
                    member.Name, Name);
            }
        }

        return items.Count == 0 ? MemoryRecall.Empty : new MemoryRecall(items, ran);
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
