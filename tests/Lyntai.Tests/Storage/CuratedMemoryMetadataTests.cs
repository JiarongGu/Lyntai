using Lyntai.Storage;

namespace Lyntai.Tests.Storage;

/// <summary>The curated-memory metadata accessor. CMEM6 retired the purpose-built <c>Source</c>/<c>Title</c>
/// columns into one arbitrary <c>string→string</c> map, which left every consumer hand-unpacking
/// <c>entry.Metadata!["source"]</c> — an indexer that throws on a missing key and NREs on the (very common)
/// no-metadata entry. The accessor is the null-safe read of that map; it deliberately does NOT re-privilege
/// any single key back into a typed member.</summary>
public class CuratedMemoryMetadataTests
{
    private static CuratedMemory Entry(IReadOnlyDictionary<string, string>? metadata) =>
        new(1, "note", "content", Enabled: true, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
            Metadata: metadata);

    [Fact]
    public void MetadataValue_reads_a_present_key()
    {
        var entry = Entry(new Dictionary<string, string> { ["source"] = "owner", ["title"] = "House style" });

        Assert.Equal("owner", entry.MetadataValue("source"));
        Assert.Equal("House style", entry.MetadataValue("title"));
    }

    [Fact]
    public void MetadataValue_returns_null_for_a_missing_key()
    {
        var entry = Entry(new Dictionary<string, string> { ["source"] = "owner" });

        Assert.Null(entry.MetadataValue("author"));
    }

    [Fact]
    public void MetadataValue_returns_null_when_the_entry_carries_no_metadata_at_all()
    {
        // the NRE consumers hit today: Metadata is null for every entry written without any
        Assert.Null(Entry(null).MetadataValue("source"));
        Assert.Null(Entry(new Dictionary<string, string>()).MetadataValue("source"));
    }

    [Fact]
    public void MetadataValue_honours_the_maps_own_comparer()
    {
        // the stored form (CuratedMetadataJson) is ordinal; a consumer that builds a case-insensitive map
        // keeps its own semantics — the accessor adds no comparer of its own
        var ordinal = Entry(new Dictionary<string, string>(StringComparer.Ordinal) { ["source"] = "owner" });
        var ignoreCase = Entry(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["source"] = "owner" });

        Assert.Null(ordinal.MetadataValue("Source"));
        Assert.Equal("owner", ignoreCase.MetadataValue("Source"));
    }

    [Fact]
    public void MetadataValue_round_trips_through_the_stored_json_form()
    {
        var stored = CuratedMetadataJson.Serialize(new Dictionary<string, string> { ["source"] = "agent" });
        var entry = Entry(CuratedMetadataJson.Deserialize(stored));

        Assert.Equal("agent", entry.MetadataValue("source"));
    }
}
