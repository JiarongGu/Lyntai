namespace Lyntai.Storage;

/// <summary>Reading conveniences over <see cref="CuratedMemory"/>.</summary>
public static class CuratedMemoryExtensions
{
    /// <summary>The value stored under <paramref name="key"/> in <see cref="CuratedMemory.Metadata"/>, or
    /// <c>null</c> when the entry carries no metadata at all or has no such key. The null-safe read of a
    /// map that is null on every entry written without metadata — <c>entry.Metadata!["source"]</c> both
    /// throws on a missing key and NREs on that very common case.
    /// <para>Conventional keys are <c>title</c>, <c>source</c>, <c>author</c>, <c>category</c> (see
    /// <see cref="CuratedMemory.Metadata"/>), but they are the APP's convention, not the library's: there is
    /// deliberately no <c>Source</c> member here. CMEM6 retired the purpose-built <c>Source</c>/<c>Title</c>
    /// columns into one arbitrary map precisely so a new payload field needs no schema or API change, and a
    /// typed accessor per key would re-privilege the same handful of names one layer up.</para>
    /// <para>Key comparison is whatever the map itself uses — the stored form
    /// (<see cref="CuratedMetadataJson"/>) is ordinal; a consumer-built map keeps its own comparer.</para></summary>
    public static string? MetadataValue(this CuratedMemory entry, string key)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(key);
        return entry.Metadata is { } metadata && metadata.TryGetValue(key, out var value) ? value : null;
    }
}
