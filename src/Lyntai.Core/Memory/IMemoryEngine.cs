using Lyntai.Inference;
namespace Lyntai.Memory;

/// <summary>
/// A named memory system: something that can be told a fact and asked for the relevant ones. Registered as
/// a DI collection and keyed by <see cref="Name"/> through <see cref="IMemoryEngineFactory"/> — the same
/// variation-point shape as <see cref="Lyntai.Inference.IModelProvider"/> keyed by <c>Id</c> and picked by
/// <see cref="Lyntai.Inference.ITextRouter"/>. Adding a kind of memory is a class plus a registration, never an
/// edit to a conditional.
/// <para>Several engines coexist in one application, so a chat memory and a project memory can differ in
/// content, retention and purpose without either wrapping the other.</para>
/// </summary>
public interface IMemoryEngine
{
    /// <summary>Unique within the container. Hierarchical for a member of a composite
    /// ("project/glossary"), which is what lets <see cref="MemoryRef"/> route unambiguously.</summary>
    string Name { get; }

    /// <summary>Which grades this engine can actually store. A store with a grade column reports both; a
    /// store whose grade is a constant of the store reports only that one. A composite reports the union
    /// of its members' and routes an incoming write to a member that can hold it.</summary>
    MemoryGrades Supported { get; }


    /// <summary>Store a fact, and report what the write did.
    /// <para><b>Surfaces failures</b> — a silently lost write is worse than a throw the caller can see,
    /// which is the asymmetry <see cref="ISemanticMemory"/> already documents. Throws
    /// <see cref="NotSupportedException"/> rather than downgrading a grade this engine cannot store:
    /// accepting an authoritative write and keeping it as associative would defeat the whole point of the
    /// grade split.</para>
    /// <para><b>Reports where the entry landed.</b> <see cref="MemoryWriteResult.Ran"/> names each storage tier
    /// that took the write, and the vector index when this write's vector reached it; an index that failed or
    /// had nothing to run on is absent, while a storage tier that fails throws, as above — the write side of
    /// <see cref="MemoryRecall.Ran"/>. An engine's other best-effort steps (the graph engine's annotation,
    /// subject index, similarity links and salience) are logged and NOT reported; the stored node's
    /// <see cref="GraphNode.ProvenanceSalience"/> names the policies behind its STORED signals, not what this
    /// write's salience did.</para></summary>
    Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default);

    /// <summary>Recall relevant facts.
    /// <para><b>Fails open</b> — a storage outage yields an empty result carrying
    /// <see cref="MemorySources.None"/>, never a throw. Only the CALLER's cancellation propagates, and the
    /// test is <c>ct.IsCancellationRequested</c> rather than the exception's type: a component's OWN deadline
    /// arrives as a <see cref="TaskCanceledException"/>, which IS an
    /// <see cref="OperationCanceledException"/> and says nothing about the caller, so it is a storage outage
    /// like any other.</para>
    /// <para><b>A recall MUTATES.</b> An engine may reinforce what it returned and link those entries to one
    /// another, so asking the same question twice is not asking it twice under the same conditions. The
    /// consequence for anyone MEASURING: an A/B over this method has to be paired and counterbalanced — each
    /// query asked under both arms back to back with the order alternating — because running one arm to
    /// completion and then the other compares a cold graph against one the first arm warmed, and the bias
    /// lands silently on whichever arm ran second.</para></summary>
    Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default);
}

/// <summary>How exact a piece of remembered material is, which decides whether it may decay, be summarised
/// to a headline, or be crowded out of a prompt by higher-ranked content.</summary>
public enum MemoryGrade
{
    /// <summary>Take the grade from the engine's own role. The default, so a caller that does not care
    /// never encounters the concept.</summary>
    Inherit = 0,

    /// <summary>Recalled context: may decay, may be summarised to a headline, and spends whatever budget
    /// is left after exact material has been allocated.</summary>
    Associative = 1,

    /// <summary>An exact fact: never decays, is never truncated, holds a reserved budget ahead of
    /// associative material, and renders in its own labelled section.</summary>
    Authoritative = 2,
}

/// <summary>The set of grades an engine can store — see <see cref="IMemoryEngine.Supported"/>.</summary>
[Flags]
public enum MemoryGrades
{
    /// <summary>Stores nothing (a read-only engine).</summary>
    None = 0,

    /// <summary>Can store <see cref="MemoryGrade.Associative"/> material.</summary>
    Associative = 1,

    /// <summary>Can store <see cref="MemoryGrade.Authoritative"/> material.</summary>
    Authoritative = 2,
}

/// <summary>Which tiers actually ran — on a recall, which produced a result; on a write
/// (<see cref="MemoryWriteResult.Ran"/>), which took it. Reported on every recall and every write. Test a
/// member with <see cref="Enum.HasFlag"/> rather than comparing the whole value: members may be added.</summary>
[Flags]
public enum MemorySources
{
    /// <summary>On a recall, nothing ran or everything that ran faulted. On a write, no named tier took it — a
    /// storage fault throws instead.</summary>
    None = 0,

    /// <summary>The keyword/full-text tier: on a recall it produced a result; on a write it stored the
    /// entry.</summary>
    Lexical = 1,

    /// <summary>A semantic-memory member: on a recall it produced hits; on a write it stored the entry as a
    /// vector.</summary>
    Semantic = 2,

    /// <summary>A curated-catalog member: on a recall it produced hits; on a write it stored the entry.</summary>
    Curated = 4,

    /// <summary>A graph member: on a recall it produced hits; on a write it stored the entry.</summary>
    Graph = 8,

    /// <summary>The similarity tier. <b>On a recall it reports CONFIGURATION</b>: similarity-derived edge
    /// enrichment is wired for this engine. <b>On a write it reports CONTRIBUTION</b>: this write's vector was
    /// indexed, whatever happened to its similarity links.
    /// <para>Deliberately distinct from <see cref="Semantic"/>, a semantic-memory MEMBER's own tier: both need
    /// a vector backend and they fail independently, so one flag could not report both honestly.</para>
    /// <para><b>Why configuration on a recall</b>, unlike its siblings: enrichment is a WRITE-side tier. It
    /// creates edges, which by the time a recall traverses them are indistinguishable from the ones
    /// co-activation wrote. What its presence buys is the distinction the whole enum exists for — a caller
    /// seeing no linked material can tell "nothing similar was ever found" from "similarity is not configured
    /// here".</para></summary>
    Similarity = 16,
    /// <summary>The annotation tier, which records what an entry is about. <b>On a write it reports
    /// CONTRIBUTION</b>: the annotator answered for this write — with subjects or with none — and what it
    /// answered was recorded. Absent when no annotator is wired, when it failed or timed out, or when recording
    /// its subjects failed, since the entry is then stored without them for good: a rebuild that needs them
    /// retries the write. <b>On a recall it reports CONFIGURATION</b>: an annotator is wired, as
    /// <see cref="Similarity"/> does, and for the same reason — its edges are indistinguishable by then.</summary>
    Annotation = 32,
}

/// <summary>An entry's address.
/// <para><see cref="Engine"/> is the name of the engine that OWNS the entry — for a member of a composite
/// this is the member's hierarchical name, never the composite's, which is what makes expansion and
/// linking route unambiguously.</para>
/// <para><see cref="Id"/> is store-defined where the store has one. An engine over a store whose write
/// returns no identifier (<see cref="Lyntai.Storage.IMemoryStore"/>, <see cref="ISemanticMemory"/>) keys by a
/// SHA-256 over the length-framed (task, scope, content) triple — the same entry under another task or scope
/// is another entry, while re-remembering identical content in one place refreshes rather than
/// duplicates.</para></summary>
/// <param name="Engine">The owning engine's <see cref="IMemoryEngine.Name"/>.</param>
/// <param name="Id">Opaque, and stable within that engine.</param>
public readonly record struct MemoryRef(string Engine, string Id);
