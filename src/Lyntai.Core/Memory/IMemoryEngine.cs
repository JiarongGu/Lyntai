namespace Lyntai.Memory;

/// <summary>
/// A named memory system: something that can be told a fact and asked for the relevant ones. Registered as
/// a DI collection and keyed by <see cref="Name"/> through <c>IMemoryEngineFactory</c> — the same
/// variation-point shape as <see cref="Lyntai.Llm.ILlmProvider"/> keyed by <c>Id</c> and picked by
/// <see cref="Lyntai.Llm.ILlmRouter"/>. Adding a kind of memory is a class plus a registration, never an
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

    /// <summary>Store a fact and return its address.
    /// <para><b>Surfaces failures</b> — a silently lost write is worse than a throw the caller can see,
    /// which is the asymmetry <see cref="ISemanticMemory"/> already documents. Throws
    /// <see cref="NotSupportedException"/> rather than downgrading a grade this engine cannot store:
    /// accepting an authoritative write and keeping it as associative would defeat the whole point of the
    /// grade split.</para></summary>
    Task<MemoryRef> RememberAsync(MemoryWrite write, CancellationToken ct = default);

    /// <summary>Recall relevant facts.
    /// <para><b>Fails open</b> — a storage outage yields an empty result carrying
    /// <see cref="MemorySources.None"/>, never a throw. Only <see cref="OperationCanceledException"/>
    /// propagates, because cancellation belongs to the caller.</para></summary>
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

/// <summary>Which retrieval tiers actually ran, so a caller can tell "nothing matched" from "that source is
/// not configured". Reported on every recall.</summary>
[Flags]
public enum MemorySources
{
    /// <summary>Nothing ran, or everything that ran faulted.</summary>
    None = 0,

    /// <summary>The keyword/full-text tier produced a result.</summary>
    Lexical = 1,

    /// <summary>A semantic-memory member produced hits.</summary>
    Semantic = 2,

    /// <summary>A curated-catalog member produced hits.</summary>
    Curated = 4,

    /// <summary>A graph member produced hits.</summary>
    Graph = 8,

    /// <summary>Similarity-derived edge enrichment ran — deliberately distinct from
    /// <see cref="Semantic"/>, which means a semantic-memory MEMBER produced hits. Both need an embedder
    /// and they fail independently, so one flag could not report both honestly.</summary>
    Similarity = 16,
}

/// <summary>An entry's address.
/// <para><see cref="Engine"/> is the name of the engine that OWNS the entry — for a member of a composite
/// this is the member's hierarchical name, never the composite's, which is what makes expansion and
/// linking route unambiguously.</para>
/// <para><see cref="Id"/> is store-defined where the store has one. An engine over a store whose write
/// returns no identifier (<see cref="Lyntai.Storage.IMemoryStore"/>, <see cref="ISemanticMemory"/>) keys by
/// the SHA-256 hex of the content — which is also how those stores define identity, since re-remembering
/// identical content refreshes rather than duplicates.</para></summary>
/// <param name="Engine">The owning engine's <see cref="IMemoryEngine.Name"/>.</param>
/// <param name="Id">Opaque, and stable within that engine.</param>
public readonly record struct MemoryRef(string Engine, string Id);
