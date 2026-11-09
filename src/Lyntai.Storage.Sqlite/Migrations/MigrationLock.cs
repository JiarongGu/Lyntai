using System.Security.Cryptography;
using System.Text;

namespace Lyntai.Storage.Sqlite.Migrations;

/// <summary>A machine-wide lock around the migration passes of ONE database file, so two processes migrating an
/// empty or old database take turns instead of each reading the version table as unapplied. Held on the calling
/// thread (a mutex is owned by its thread), which is why the runner's passes stay synchronous inside it.</summary>
internal sealed class MigrationLock : IDisposable
{
    private readonly Mutex _mutex;

    private MigrationLock(Mutex mutex) => _mutex = mutex;

    /// <summary>Waits for the lock on <paramref name="dbPath"/>'s full path. A holder that died without
    /// releasing leaves an abandoned mutex, which the waiter now owns: the migrations are idempotent, and the
    /// version table says what the dead run finished.</summary>
    public static MigrationLock Acquire(string dbPath)
    {
        var mutex = new Mutex(false, NameFor(dbPath));
        try { mutex.WaitOne(); }
        catch (AbandonedMutexException) { /* owned now, as the summary says */ }
        return new MigrationLock(mutex);
    }

    // The name carries the hash of the full path, so one file reached by a relative and an absolute spelling is
    // one lock. Windows paths compare case-insensitively; a Unix path does not.
    internal static string NameFor(string dbPath)
    {
        var full = Path.GetFullPath(dbPath);
        if (OperatingSystem.IsWindows()) full = full.ToUpperInvariant();
        return "Lyntai.Migrate." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full)));
    }

    public void Dispose()
    {
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
