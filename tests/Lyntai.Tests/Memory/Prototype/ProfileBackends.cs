using Lyntai.Storage;
using Lyntai.Storage.FileSystem;
using Lyntai.Storage.InMemory;
using Lyntai.Storage.Sqlite;
using Lyntai.Tests.Storage;
using Lyntai.Tests.Storage.FileSystem;

namespace Lyntai.Tests.Memory.Prototype;

/// <summary>Every store that implements <see cref="IRevisionedCuratedMemoryStore"/>, which is what D221's "the chosen
/// backends" reads as: the projection's correction needs that interface and nothing else.</summary>
internal static class ProfileBackends
{
    internal static readonly string[] All = ["sqlite", "files", "memory", "postgres"];

    /// <summary>A fresh, empty store for one site, and the lease that releases it. "postgres" SKIPS when the container
    /// never started.</summary>
    internal static (ICuratedMemoryStore Store, IDisposable Lease) Open(string backend, PostgresFixture pg)
    {
        switch (backend)
        {
            case "postgres":
                return ProfilePostgresSites.For(pg).Open();
            case "sqlite":
                var db = new TempDb();
                return (new SqliteCuratedMemoryStore(db.Factory), db);
            case "files":
                var root = new TempRoot();
                return (new FileSystemCuratedMemoryStore(root.Root), root);
            case "memory":
                return (new InMemoryCuratedMemoryStore(), new NoLease());
            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "no such backend");
        }
    }

    private sealed class NoLease : IDisposable
    {
        public void Dispose()
        {
        }
    }
}
