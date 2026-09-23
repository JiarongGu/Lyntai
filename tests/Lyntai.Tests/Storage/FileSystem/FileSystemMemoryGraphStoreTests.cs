using Lyntai.Memory;
using Lyntai.Storage.FileSystem;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Memory;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>Every <see cref="MemoryGraphStoreContract"/> fact against the file-system backend, a fresh root per
/// case, plus the failed writes the contract cannot provoke. The contract runs one live store; what survives a
/// restart is <c>FileSystemGraphRestartTests</c>.</summary>
public class FileSystemMemoryGraphStoreTests : IDisposable
{
    private readonly TempRoot _root = new();

    public void Dispose() => _root.Dispose();

    [Theory]
    [MemberData(nameof(MemoryGraphStoreFacts.Names), MemberType = typeof(MemoryGraphStoreFacts))]
    public Task Contract(string fact) =>
        MemoryGraphStoreFacts.RunAsync(fact, clock => new FileSystemMemoryGraphStore(_root.Root, clock), fact);

    private static GraphNodeWrite Note(string engine, string content) =>
        new(engine, "task", "s", "h", content, MemoryGrade.Associative, InitialStability: 3, Advance: 1, Metadata: null);

    private string EngineDirectory(string engine) => Path.Combine(_root.Directory, "graph", RecordName.For(engine));

    [Fact]
    public async Task A_failed_memory_file_write_never_reissues_its_journaled_id()
    {
        var store = new FileSystemMemoryGraphStore(_root.Root);
        var first = await store.UpsertAsync(Note("zeta", "one"));
        // a directory where the next memory's file would land, so its rename cannot
        Directory.CreateDirectory(Path.Combine(EngineDirectory("zeta"), RecordName.For("task"), RecordName.For("s"),
            FileSystemRoot.IdFile(first + 1)));

        var failed = await Record.ExceptionAsync(() => store.UpsertAsync(Note("zeta", "two")));
        Assert.True(failed is IOException or UnauthorizedAccessException, $"expected the file write to fail: {failed}");

        // the journal holds first + 1 already, so no other memory — in any engine — may be given it
        var next = await store.UpsertAsync(Note("alpha", "three"));
        Assert.True(next > first + 1, $"id {next} was re-issued after its journal line was written");

        var reopened = new FileSystemMemoryGraphStore(_root.Reopen());
        Assert.Equal(["one"], (await reopened.SeedAsync("zeta", "task", null, null, 10)).Select(n => n.Content));
        var alpha = await reopened.GetAsync("alpha", next);
        Assert.Equal(("three", 0), (alpha!.Content, alpha.RecallCount));
    }

    [Fact]
    public async Task A_memory_reads_its_state_from_its_own_engines_journal_only()
    {
        var store = new FileSystemMemoryGraphStore(_root.Root);
        var own = await store.UpsertAsync(Note("alpha", "own"));
        await store.TouchAsync("alpha", [new GraphTouch(own, 7)]);
        await store.UpsertAsync(Note("zeta", "other"));
        _root.Root.Dispose();
        // a stray line for the same id in an engine directory that sorts AFTER the memory's own — what a failed
        // write elsewhere once left behind
        File.AppendAllText(Path.Combine(EngineDirectory("zeta"), "state", "journal.jsonl"), GraphLines.Node(own,
            new GraphNodeState(99, 42, 0, MemorySignals.Empty, 0, 0, DateTimeOffset.UnixEpoch, 0, 0, 5)) + "\n");

        var node = await new FileSystemMemoryGraphStore(_root.Reopen()).GetAsync("alpha", own);

        Assert.Equal((1, 7d), (node!.RecallCount, node.Stability));
    }
}
