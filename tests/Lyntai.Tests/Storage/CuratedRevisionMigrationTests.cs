using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>
/// Every contract fact runs on a fresh database, where the revision column's default reaches no row. This
/// writes an entry under the schema BEFORE <c>M202610241721_CuratedRevision</c>, finishes migrating, and
/// asserts the entry reads revision 1 and can be updated on it — so a migration that left old rows at 0 (which
/// reads as "this store does not track revisions") or NULL fails here.
/// </summary>
public sealed class CuratedRevisionMigrationTests : IDisposable
{
    private const long BeforeCuratedRevision = 202609262244;
    private readonly TempDbPath _path = new("curated-revision");

    public void Dispose() => _path.Dispose();

    [Fact]
    public async Task An_entry_written_before_revisions_reads_revision_one_and_updates_on_it()
    {
        var connectionString = new SqliteConnectionStringBuilder { DataSource = _path.Path }.ToString();
        MemoryAgePrimitivesBackfillTests.SeedPragmas(connectionString);
        MemoryAgePrimitivesBackfillTests.RunTo(connectionString, BeforeCuratedRevision);

        long id;
        using (var conn = new SqliteConnection(connectionString))
        {
            conn.Open();
            id = conn.ExecuteScalar<long>("""
                INSERT INTO lyntai_curated_memory (kind, content, enabled, created_at, updated_at)
                VALUES ('style', 'written before revisions', 1, '2026-01-01T00:00:00Z', '2026-01-01T00:00:00Z')
                RETURNING id
                """);
        }

        MemoryAgePrimitivesBackfillTests.RunTo(connectionString, null);

        ICuratedMemoryStore store = new SqliteCuratedMemoryStore(new SqliteConnectionFactory(_path.Path));
        Assert.Equal(1, (await store.GetAsync(id))!.Revision);
        Assert.Equal(CuratedUpdateOutcome.Updated, await Assert.IsAssignableFrom<IRevisionedCuratedMemoryStore>(store)
            .UpdateAsync(id, expectedRevision: 1, content: "edited"));
        Assert.Equal(2, (await store.GetAsync(id))!.Revision);
    }
}
