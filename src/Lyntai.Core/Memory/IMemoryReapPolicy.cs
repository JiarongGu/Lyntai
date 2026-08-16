namespace Lyntai.Memory;

/// <summary>Which kind of removal a blend is considering. Asked separately because the two are different
/// acts with different owners: a FORGET is a user's withdrawal of their own data, a PRUNE is an operator's
/// or a scheduler's bounding of an ever-growing store.</summary>
public enum MemoryReapKind
{
    /// <summary>An unconditional removal of a (task, scope) — what an application calls when a user
    /// withdraws consent or a task ends.</summary>
    Forget,

    /// <summary>A selective removal of whatever qualifies — capacity management.</summary>
    Prune,
}

/// <summary>Decides which members of a blend a reap actually visits.
///
/// <para><b>Eligibility is a DEPLOYMENT question, which is why it is a policy and not a property of an
/// engine.</b> Whether a curated section holds material a user may withdraw is not something this library
/// can know: one application's glossary is operator-authored boilerplate, another's holds preferences the
/// user typed. Baking the answer into the engine class would state a fact about the HOST inside a type the
/// host did not write — the shape <c>.claude/knowledge/model-decoupling.md</c> exists to prevent.</para>
///
/// <para><b>It is NOT the same question as the capability interfaces</b> (<see cref="IForgettableMemory"/>,
/// <see cref="IPrunableMemory"/>), and they are deliberately not merged. Those answer "can this store delete
/// at all", which no configuration changes — a vector store either has a delete API or it does not. This
/// answers "should this member's content be in scope", which only the deployment knows. Conflating them
/// would let a host configure away a genuine GAP, and a gap silently skipped is the defect
/// <c>docs/DECISIONS.md</c> D63 records.</para>
///
/// <para>Asked per KIND as well as per member, because a host can legitimately want a section excluded from
/// an automatic prune and included in an explicit consent withdrawal — or the reverse. A single boolean
/// could not say either.</para></summary>
public interface IMemoryReapPolicy
{
    /// <summary>Whether <paramref name="member"/> is in scope for a <paramref name="kind"/> reap. A member
    /// answered false is SKIPPED — never a reason to refuse the whole verb.</summary>
    bool Includes(IMemoryEngine member, MemoryReapKind kind);
}

/// <summary>The shipped default: a member that can hold ONLY authoritative material is out of scope for
/// both kinds of reap; everything else is in scope.
///
/// <para><b>Why that test rather than the engine's type.</b> <see cref="MemoryGrades"/> is a property an
/// engine already declares, and the grade split exists precisely to separate operator-maintained exact facts
/// from decaying associative material — so an authoritative-ONLY engine is a curated catalogue by
/// construction, whoever wrote it. Keying on the concrete type would have been a conditional that must be
/// edited to add a backend, and would miss a BYO catalogue engine entirely.</para>
///
/// <para><b>Stated as what it is: a heuristic, appropriate BECAUSE it is a default.</b> It reproduces the
/// conventional roles of the shipped engines — curated out, graph/lexical/semantic in — and a deployment
/// whose arrangement differs registers its own <see cref="IMemoryReapPolicy"/>. That is the one line a host
/// writes when the guess is wrong, which is what makes a guess acceptable here and not in the engine
/// itself.</para></summary>
public sealed class DefaultMemoryReapPolicy : IMemoryReapPolicy
{
    /// <inheritdoc />
    public bool Includes(IMemoryEngine member, MemoryReapKind kind)
    {
        ArgumentNullException.ThrowIfNull(member);
        return member.Supported != MemoryGrades.Authoritative;
    }
}
