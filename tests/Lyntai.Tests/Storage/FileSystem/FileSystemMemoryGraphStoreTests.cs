using Lyntai.Storage.FileSystem;
using Lyntai.Tests.Memory;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against the file-system backend, a fresh root per
/// case. The contract runs one live store; what survives a restart is <c>FileSystemGraphRestartTests</c>.</summary>
public class FileSystemMemoryGraphStoreTests : IDisposable
{
    private readonly TempRoot _root = new();

    public void Dispose() => _root.Dispose();

    [Theory]
    [MemberData(nameof(MemoryGraphStoreFacts.Names), MemberType = typeof(MemoryGraphStoreFacts))]
    public Task Contract(string fact) =>
        MemoryGraphStoreFacts.RunAsync(fact, clock => new FileSystemMemoryGraphStore(_root.Root, clock), fact);
}
