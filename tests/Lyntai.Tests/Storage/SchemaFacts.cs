using Dapper;
using Lyntai.Storage;
using Lyntai.Storage.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>What a fully migrated database carries, written ONCE: a new migration is one edit here, not a
/// literal in every test that counts. The versions are listed rather than reflected from the
/// <c>[Migration]</c> types, because a RENUMBERED migration is the trap a list catches and a reflection
/// cannot (an applied number is never re-run).</summary>
internal static class SchemaFacts
{
    /// <summary>The SQLite set, in order: the nine 1.0 baselines, MemoryGraph, MemoryRetentionModel,
    /// JobSlots, JobStageDetail, CuratedRevision, UsageReservation, MemoryValidTime, VectorBlob.</summary>
    public static readonly long[] SqliteVersions =
    [
        202607280001, 202607280002, 202607280003, 202607280004, 202607280005, 202607280006, 202607280007,
        202607280008, 202607280009, 202608081215, 202608121100, 202608161159, 202609262244, 202610241721,
        202610241822, 202610251239, 202610281422,
    ];

    /// <summary>Postgres carries MemoryHeadlineSearch, a trigram index on <c>headline</c> that SQLite needs no
    /// counterpart for because its FTS5 mirror already indexes that column, and lacks VectorBlob, which has no
    /// counterpart there because <c>PostgresVectorStore</c> creates its own pgvector table outside the migrations.
    /// Migrations are per-backend projects, so the sets are allowed to differ.</summary>
    public static readonly long[] PostgresVersions =
        [.. SqliteVersions.Where(v => v != 202610281422).Append(202608152310).Order()];

    public static bool SqliteTableExists(SqliteConnectionFactory factory, string table)
    {
        using var conn = factory.Open();
        return conn.ExecuteScalar<long>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = @table", new { table }) > 0;
    }

    public static async Task<bool> PostgresTableExists(IDbConnectionFactory factory, string table)
    {
        using var conn = factory.Open();
        return await conn.ExecuteScalarAsync<bool>(
            "SELECT EXISTS (SELECT 1 FROM pg_tables WHERE schemaname = 'public' AND tablename = @table)",
            new { table });
    }
}
