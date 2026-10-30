namespace Lyntai.Memory;

/// <summary>How a live entry came to be offered as one a write may update.</summary>
[Flags]
public enum MemoryUpdateBasis
{
    /// <summary>The write's words appear in it — the store's own lexical match.</summary>
    Lexical = 1,
    /// <summary>Its vector is near the write's, which needs an embedder and a vector index.</summary>
    Similarity = 2,
}

/// <summary>One live entry a write may update, and why it was offered.</summary>
/// <param name="Item">The entry as a recall returns it, so its <see cref="MemoryItem.Reference"/> and
/// <see cref="MemoryItem.Revision"/> are what <c>Supersedes</c> or an end takes.</param>
/// <param name="Basis">Every basis that found it.</param>
public sealed record MemoryUpdateCandidate(MemoryItem Item, MemoryUpdateBasis Basis);

/// <summary>OPTIONAL capability: show a writer the live entries a write may update, and apply none.
/// <para>A candidate list is a SUGGESTION. Nothing here supersedes, ends, links, reinforces or writes, so a list the
/// writer reads demotes nothing; the writer confirms through <see cref="MemoryWrite.Supersedes"/> or
/// <see cref="IEndableMemory.EndAsync"/> (<c>docs/DECISIONS.md</c> D239). A composite ALWAYS implements this and
/// routes by the write, as a remember does.</para></summary>
public interface IMemoryCandidates
{
    /// <summary>The entries of the write's own task and scope that are valid now and are not the write itself, best
    /// first, at most <paramref name="limit"/>. Reads without reinforcing or linking anything, and makes no model call
    /// beyond the one embed an enriched engine already makes for a write. An ended entry, another scope and another
    /// task never appear. Callable before the write or after it: an entry the write is (same content) is left out
    /// either way.</summary>
    /// <param name="write">The write to find updates for; nothing of it is stored.</param>
    /// <param name="limit">The most to return; zero or less returns none. The default of ten is measured, not borrowed: on
    /// <c>docs/memory-measurements.md</c> §5 (<c>update-candidates-lexical-haystack-n70</c>) the lexical basis puts the true
    /// predecessor in the first four for 54% of pairs and in the first ten for 89%.</param>
    /// <param name="ct">Cancellation, which is never swallowed.</param>
    Task<IReadOnlyList<MemoryUpdateCandidate>> FindUpdatesAsync(MemoryWrite write, int limit = 10,
        CancellationToken ct = default);
}
