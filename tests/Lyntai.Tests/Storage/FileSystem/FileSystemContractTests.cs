using Lyntai.Storage;
using Lyntai.Storage.FileSystem;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>The <see cref="KeyValueStoreContract"/> against the file-system backend over a per-test root.</summary>
public class FileSystemKeyValueStoreContractTests : KeyValueStoreContractFacts, IDisposable
{
    private readonly TempRoot _root = new();
    protected override IKeyValueStore NewStore() => new FileSystemKeyValueStore(_root.Root);
    public void Dispose() => _root.Dispose();
}

/// <summary>The <see cref="PromptVersionStoreContract"/> against the file-system backend.</summary>
public class FileSystemPromptVersionStoreContractTests : PromptVersionStoreContractFacts, IDisposable
{
    private readonly TempRoot _root = new();
    protected override IPromptVersionStore NewStore() => new FileSystemPromptVersionStore(_root.Root);
    public void Dispose() => _root.Dispose();
}

/// <summary>The <see cref="ConversationStoreContract"/> against the file-system backend.</summary>
public class FileSystemConversationStoreContractTests : ConversationStoreContractFacts, IDisposable
{
    private readonly TempRoot _root = new();
    protected override IConversationStore NewStore() => new FileSystemConversationStore(_root.Root);
    public void Dispose() => _root.Dispose();
}

/// <summary>The <see cref="MemoryStoreContract"/> against the file-system backend, on the shared clock.</summary>
public class FileSystemMemoryStoreContractTests : MemoryStoreContractFacts, IDisposable
{
    private readonly TempRoot _root = new();
    protected override IMemoryStore New() => new FileSystemMemoryStore(_root.Root, Options, clock: () => Now);
    protected override IMemoryStore NewWith(MemoryEvictionPolicy p) =>
        new FileSystemMemoryStore(_root.Root, new LyntaiOptions { MemoryEviction = p, MemoryRecallLimit = 100 }, clock: () => Now);
    public void Dispose() => _root.Dispose();
}

/// <summary>The <see cref="CuratedMemoryStoreContract"/> against the file-system backend.</summary>
public class FileSystemCuratedMemoryStoreContractTests : CuratedMemoryStoreContractFacts, IDisposable
{
    private readonly TempRoot _root = new();
    protected override ICuratedMemoryStore NewStore() => new FileSystemCuratedMemoryStore(_root.Root);
    public void Dispose() => _root.Dispose();
}
