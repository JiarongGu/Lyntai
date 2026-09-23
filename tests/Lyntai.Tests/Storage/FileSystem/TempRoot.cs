using Lyntai.Storage.FileSystem;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>Per-test file-system root under devtools/_test-scratch (family rule: scratch under devtools/_*,
/// never OS temp), owned for the test's lifetime and deleted on dispose. <see cref="Reopen"/> releases the
/// ownership and takes it again, which is what a process restart does to a root.</summary>
public sealed class TempRoot : IDisposable
{
    public TempRoot() => Root = new FileSystemRoot(Directory);

    public string Directory { get; } = Path.Combine(TestPaths.TestScratchDir, $"fs-{Guid.NewGuid():N}");

    internal FileSystemRoot Root { get; private set; }

    internal FileSystemRoot Reopen()
    {
        Root.Dispose();
        return Root = new FileSystemRoot(Directory);
    }

    public void Dispose()
    {
        Root.Dispose();
        try { System.IO.Directory.Delete(Directory, recursive: true); } catch (IOException) { /* gitignored scratch */ }
    }
}
