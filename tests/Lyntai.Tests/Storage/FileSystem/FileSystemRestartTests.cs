using Lyntai.Storage;
using Lyntai.Storage.FileSystem;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>What the contract suites cannot see, because each runs one live store: the data survives the
/// process. Every fact writes, releases the root as a restart would, and reads back through a NEW store.</summary>
public class FileSystemRestartTests : IDisposable
{
    private readonly TempRoot _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Keys_and_values_survive_a_restart()
    {
        var before = new FileSystemKeyValueStore(_temp.Root);
        await before.SetAsync("a", "one\nwith a newline");
        await before.SetAsync("b", "灵台");
        await before.DeleteAsync("b");

        var after = new FileSystemKeyValueStore(_temp.Reopen());

        Assert.Equal("one\nwith a newline", await after.GetAsync("a"));
        Assert.Equal(["a"], await after.ListKeysAsync());
    }

    [Fact]
    public async Task A_value_edited_by_hand_while_no_process_owned_the_root_is_read_on_the_next_start()
    {
        await new FileSystemKeyValueStore(_temp.Root).SetAsync("lyntai.model.chat", "old-model");
        var file = Directory.GetFiles(Path.Combine(_temp.Directory, "kv")).Single();

        _temp.Root.Dispose();
        File.WriteAllText(file, File.ReadAllText(file).Replace("old-model", "new-model", StringComparison.Ordinal));

        Assert.Equal("new-model", await new FileSystemKeyValueStore(_temp.Reopen()).GetAsync("lyntai.model.chat"));
    }

    [Fact]
    public async Task Prompt_history_and_the_active_revision_survive_a_restart()
    {
        var before = new FileSystemPromptVersionStore(_temp.Root);
        await before.SaveAsync("greet", "v1 {{name}}", "ann");
        await before.SaveAsync("greet", "v2 {{name}}");
        await before.RollbackAsync("greet", 1);

        var after = new FileSystemPromptVersionStore(_temp.Reopen());

        var active = await after.GetActiveAsync("greet");
        Assert.Equal((1, "v1 {{name}}", "ann", true), (active!.Version, active.Template, active.Author, active.IsActive));
        Assert.Equal([2, 1], (await after.HistoryAsync("greet")).Select(v => v.Version));
        Assert.Equal(3, (await after.SaveAsync("greet", "v3")).Version);
    }

    [Fact]
    public async Task A_thread_and_its_events_survive_a_restart_and_the_sequence_continues()
    {
        var before = new FileSystemConversationStore(_temp.Root);
        await before.CreateThreadAsync("t1", "title", """{"phase":"plan"}""");
        await before.AppendMessageAsync("t1", "user", "hello");
        await before.AppendMessageAsync("t1", "tool-call", """{"name":"x"}""", """{"model":"m"}""");

        var after = new FileSystemConversationStore(_temp.Reopen());

        var thread = await after.GetThreadAsync("t1");
        Assert.Equal(("title", """{"phase":"plan"}"""), (thread!.Title, thread.Metadata));
        var messages = await after.GetMessagesAsync("t1");
        Assert.Equal([(1L, "user", "hello", (string?)null), (2L, "tool-call", """{"name":"x"}""", """{"model":"m"}""")],
            messages.Select(m => (m.Seq, m.Kind, m.Payload, m.Metadata)));
        Assert.Equal(3, (await after.AppendMessageAsync("t1", "assistant", "hi")).Seq);
        await Assert.ThrowsAnyAsync<Exception>(() => after.CreateThreadAsync("t1"));
    }

    [Fact]
    public async Task Memories_survive_a_restart_with_their_expiry_and_new_ids_continue()
    {
        var now = new DateTimeOffset(2026, 9, 23, 12, 0, 0, TimeSpan.Zero);
        var options = new LyntaiOptions();
        var before = new FileSystemMemoryStore(_temp.Root, options, () => now);
        await before.RememberAsync("task", "scope", "the deploy key rotates monthly");
        await before.RememberAsync("task", "scope", "a passing thought", TimeSpan.FromMinutes(5));

        now += TimeSpan.FromMinutes(10);
        var after = new FileSystemMemoryStore(_temp.Reopen(), options, () => now);

        Assert.Equal(["the deploy key rotates monthly"], (await after.RecallAsync("task")).Select(m => m.Content));
        await after.RememberAsync("task", "scope", "a third fact");
        Assert.Equal(3, (await after.RecallAsync("task", query: "third")).Single().Id);
    }

    [Fact]
    public async Task Curated_entries_survive_a_restart_with_every_field_and_new_ids_continue()
    {
        var before = new FileSystemCuratedMemoryStore(_temp.Root);
        var id = await before.AddAsync("style", "use British spelling", taskKey: "writer", scope: "lang:en",
            metadata: new Dictionary<string, string> { ["source"] = "editor" });
        await before.UpdateAsync(id, enabled: false);

        var after = new FileSystemCuratedMemoryStore(_temp.Reopen());

        var entry = await after.GetAsync(id);
        Assert.Equal(("style", "use British spelling", false, "writer", "lang:en", "editor"),
            (entry!.Kind, entry.Content, entry.Enabled, entry.TaskKey, entry.Scope, entry.Metadata!["source"]));
        Assert.Equal([id], (await after.SearchAsync("British")).Select(e => e.Id));
        Assert.Equal(id + 1, await after.AddAsync("style", "another"));
    }
}
