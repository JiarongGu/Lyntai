using Lyntai.Storage.FileSystem;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>A save writes the revision file and THEN the active pointer, so a process killed between the two
/// leaves a revision in the history that was never active. The cut is made here by putting the pointer back to
/// what it held before the second save — the exact disk state the kill leaves — and the next start reads it.</summary>
public sealed class FileSystemPromptSaveCutTests : IDisposable
{
    private readonly TempRoot _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task A_save_cut_before_the_pointer_write_leaves_a_revision_that_was_never_active()
    {
        await new FileSystemPromptVersionStore(_temp.Root).SaveAsync("greet", "v1");
        var pointer = Directory.GetFiles(Path.Combine(_temp.Directory, "prompts"), "active.md", SearchOption.AllDirectories).Single();
        var before = File.ReadAllText(pointer);
        await new FileSystemPromptVersionStore(_temp.Root).SaveAsync("greet", "v2");
        _temp.Root.Dispose();
        File.WriteAllText(pointer, before); // the pointer write of the second save never happened

        var after = new FileSystemPromptVersionStore(_temp.Reopen());

        var history = await after.HistoryAsync("greet");
        Assert.Equal([2, 1], history.Select(v => v.Version));
        Assert.Equal([false, true], history.Select(v => v.IsActive)); // v2 is in the history and was never active
        Assert.Equal(1, (await after.GetActiveAsync("greet"))!.Version);

        var next = await after.SaveAsync("greet", "v3"); // numbered past the cut revision, which keeps its number
        Assert.Equal(3, next.Version);
        Assert.Equal(3, (await after.GetActiveAsync("greet"))!.Version);
    }
}
