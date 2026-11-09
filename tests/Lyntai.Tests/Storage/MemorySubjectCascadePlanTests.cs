using Dapper;

namespace Lyntai.Tests.Storage;

/// <summary>Deleting a node cascades to <c>lyntai_memory_subject</c> through <c>node_id</c>, and SQLite runs that
/// as <c>DELETE FROM lyntai_memory_subject WHERE node_id = ?</c> once per deleted node. The table's key leads
/// with <c>engine</c> and its lookup index with <c>engine, subject</c>, so without an index leading with
/// <c>node_id</c> every node delete scans the whole subject table.</summary>
public sealed class MemorySubjectCascadePlanTests : IDisposable
{
    private readonly TempDb _db = new();
    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task The_cascade_from_a_node_delete_is_an_index_search_not_a_scan()
    {
        await using var conn = await _db.Factory.OpenAsync();

        var plan = (await conn.QueryAsync(
            "EXPLAIN QUERY PLAN DELETE FROM lyntai_memory_subject WHERE node_id = 1"))
            .Select(row => (string)row.detail).ToList();

        Assert.DoesNotContain(plan, d => d.StartsWith("SCAN", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan, d => d.Contains("USING", StringComparison.OrdinalIgnoreCase) && d.Contains("node_id=?", StringComparison.OrdinalIgnoreCase));
    }
}
