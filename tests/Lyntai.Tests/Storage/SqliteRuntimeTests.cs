using Lyntai.Storage.Sqlite;
using Microsoft.Data.Sqlite;

namespace Lyntai.Tests.Storage;

/// <summary>Pins the SAFETY properties of the one process-global setting this package exposes. The
/// PERFORMANCE claim behind it is measured elsewhere (<c>docs/memory.md</c> §7); what a test can hold is
/// that the call is harmless whenever it is made, which is the half a consumer cannot check for themselves.
/// <para><b>The late branch is what these force deterministically.</b> Opening a connection initialises
/// SQLite, after which the setting can no longer be changed — so every assertion below runs on the branch
/// that would be dangerous if it were implemented with <c>sqlite3_shutdown</c>. The early branch cannot be
/// forced from inside a test run that has already opened a database, and is exercised by the probe and the
/// bench instead.</para></summary>
public class SqliteRuntimeTests
{
    private static void EnsureSqliteInitialised()
    {
        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT 1";
        cmd.ExecuteScalar();
    }

    [Fact]
    public void Disabling_memory_statistics_after_a_connection_reports_false_rather_than_throwing()
    {
        EnsureSqliteInitialised();

        // SQLite refuses the change once it has initialised. The contract is that this is REPORTED, never
        // forced: forcing it would mean shutting the library down underneath live connections.
        Assert.False(SqliteRuntime.DisableMemoryStatistics());
    }

    [Fact]
    public void Disabling_memory_statistics_is_idempotent()
    {
        EnsureSqliteInitialised();

        Assert.Equal(SqliteRuntime.DisableMemoryStatistics(), SqliteRuntime.DisableMemoryStatistics());
    }

    [Fact]
    public void SQLite_still_works_after_the_call_whatever_it_answered()
    {
        EnsureSqliteInitialised();
        SqliteRuntime.DisableMemoryStatistics();

        using var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "CREATE TABLE t (id INTEGER PRIMARY KEY); INSERT INTO t VALUES (7); SELECT id FROM t";

        Assert.Equal(7L, cmd.ExecuteScalar());
    }
}
