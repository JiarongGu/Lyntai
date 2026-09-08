namespace Lyntai.Storage.Sqlite;

/// <summary>Process-global SQLite settings, for a host willing to make one.
///
/// <para>Everything else in this package is per-connection or per-store. These settings belong to the
/// native SQLite library, which the whole process shares — including any SQLite the host uses directly —
/// so they are exposed here as an explicit startup call rather than as an option on a factory or a
/// builder. Nothing here is applied on your behalf.</para></summary>
public static class SqliteRuntime
{
    /// <summary>Turn off SQLite's collection of memory-allocation statistics, process-wide. Returns
    /// <see langword="true"/> if SQLite accepted the change, <see langword="false"/> if it was already too
    /// late; it never throws, and on the <see langword="false"/> path it changes nothing.
    ///
    /// <para><b>Call it at startup, before the first connection is opened</b> — before
    /// <c>UseSqliteStorage</c> runs a migration and before any store is resolved. SQLite accepts the
    /// setting only while the library is uninitialised, and the first connection initialises it. It does
    /// NOT shut SQLite down to get its way: that is undefined behaviour while a connection is live.</para>
    ///
    /// <para><b>It is worth a great deal to a store several threads read at once</b>, because maintaining
    /// those statistics takes a process-global mutex on every allocation and free and SQLite allocates
    /// heavily inside an FTS5 query — so concurrent readers serialise on the counter rather than on the
    /// database. Measured on the graph memory engine, eight concurrent recalls go from 340/s to 4,665/s
    /// and the curve stops PEAKING AT TWO WORKERS; one thread is unaffected (<c>docs/memory.md</c>
    /// §7).</para>
    ///
    /// <para><b>What it costs:</b> <c>sqlite3_memory_used</c>, <c>sqlite3_memory_highwater</c>,
    /// <c>sqlite3_status</c> and the soft and hard heap limits stop working, for the host's own SQLite as
    /// much as for this library's. Lyntai reads none of them; if you do, or if you bound SQLite's memory
    /// with a heap limit, do not call this.</para></summary>
    /// <returns><see langword="true"/> if SQLite accepted the change; <see langword="false"/> if it had
    /// already initialised, in which case nothing changed.</returns>
    public static bool DisableMemoryStatistics()
    {
        // Sets the provider without initialising SQLite, which is what leaves sqlite3_config open to us.
        // Microsoft.Data.Sqlite calls this itself on first use; calling it early is safe and idempotent.
        SQLitePCL.Batteries_V2.Init();
        return SQLitePCL.raw.sqlite3_config(SQLitePCL.raw.SQLITE_CONFIG_MEMSTATUS, 0) == SQLitePCL.raw.SQLITE_OK;
    }
}
