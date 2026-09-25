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
        // "already exists", not the unreadable-directory refusal: the reopened store LOADED the thread
        var duplicate = await Assert.ThrowsAsync<InvalidOperationException>(() => after.CreateThreadAsync("t1"));
        Assert.Contains("already exists", duplicate.Message, StringComparison.Ordinal);
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

    // ---- a file a person touched: skipped when broken, never written over, and followed when renamed ------

    private const string Broken = "a person's half-finished edit";

    private string Only(string domain, string pattern = "*.md") =>
        Directory.GetFiles(Path.Combine(_temp.Directory, domain), pattern, SearchOption.AllDirectories).Single();

    [Fact]
    public async Task A_prompt_revision_that_does_not_parse_is_never_saved_over()
    {
        await new FileSystemPromptVersionStore(_temp.Root).SaveAsync("greet", "v1");
        var revision = Only("prompts", "v0001.md");
        _temp.Root.Dispose();
        File.WriteAllText(revision, Broken);

        var after = new FileSystemPromptVersionStore(_temp.Reopen());

        Assert.Equal(2, (await after.SaveAsync("greet", "v2")).Version);
        Assert.Equal(Broken, File.ReadAllText(revision));
    }

    [Fact]
    public async Task A_prompt_whose_active_pointer_does_not_parse_is_refused_rather_than_written_over()
    {
        await new FileSystemPromptVersionStore(_temp.Root).SaveAsync("greet", "v1");
        var pointer = Only("prompts", "active.md");
        _temp.Root.Dispose();
        File.WriteAllText(pointer, Broken);

        var after = new FileSystemPromptVersionStore(_temp.Reopen());

        await Assert.ThrowsAsync<InvalidOperationException>(() => after.SaveAsync("greet", "v2"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => after.RollbackAsync("greet", 1));
        Assert.Equal(Broken, File.ReadAllText(pointer));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(pointer)!, "v*.md")); // no revision written either
    }

    [Fact]
    public async Task A_thread_whose_file_does_not_parse_is_refused_rather_than_recreated_over_it()
    {
        var before = new FileSystemConversationStore(_temp.Root);
        await before.CreateThreadAsync("t1");
        await before.AppendMessageAsync("t1", "user", "hello");
        var thread = Only("conversations", "thread.md");
        _temp.Root.Dispose();
        File.WriteAllText(thread, Broken);

        var after = new FileSystemConversationStore(_temp.Reopen());

        Assert.Null(await after.GetThreadAsync("t1"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => after.CreateThreadAsync("t1"));
        Assert.Equal(Broken, File.ReadAllText(thread));
    }

    [Fact]
    public async Task A_key_whose_file_does_not_parse_is_refused_rather_than_written_over()
    {
        await new FileSystemKeyValueStore(_temp.Root).SetAsync("k", "v");
        var file = Only("kv");
        _temp.Root.Dispose();
        File.WriteAllText(file, Broken);

        var after = new FileSystemKeyValueStore(_temp.Reopen());

        await Assert.ThrowsAsync<InvalidOperationException>(() => after.SetAsync("k", "new"));
        Assert.Equal(Broken, File.ReadAllText(file));
    }

    [Fact]
    public async Task A_deleted_key_stays_deleted_when_a_hand_made_copy_also_held_it()
    {
        await new FileSystemKeyValueStore(_temp.Root).SetAsync("k", "v");
        var file = Only("kv");
        _temp.Root.Dispose();
        File.Copy(file, Path.Combine(Path.GetDirectoryName(file)!, "zz-copy.md"));

        await new FileSystemKeyValueStore(_temp.Reopen()).DeleteAsync("k");

        Assert.Null(await new FileSystemKeyValueStore(_temp.Reopen()).GetAsync("k"));
    }

    [Fact]
    public async Task A_curated_file_renamed_by_hand_is_updated_in_place_and_stays_removed()
    {
        var id = await new FileSystemCuratedMemoryStore(_temp.Root).AddAsync("style", "use short sentences");
        var file = Only("curated");
        var renamed = Path.Combine(Path.GetDirectoryName(file)!, "style-note.md");
        _temp.Root.Dispose();
        File.Move(file, renamed);

        var after = new FileSystemCuratedMemoryStore(_temp.Reopen());
        Assert.True(await after.UpdateAsync(id, content: "use shorter sentences"));
        Assert.Equal(renamed, Only("curated"));

        Assert.True(await after.RemoveAsync(id));
        Assert.Null(await new FileSystemCuratedMemoryStore(_temp.Reopen()).GetAsync(id));
    }

    [Fact]
    public async Task A_recall_whose_access_time_cannot_be_written_still_returns_what_it_found()
    {
        var options = new LyntaiOptions();
        options.MemoryEviction.Mode = MemoryEvictionMode.Lru;
        var store = new FileSystemMemoryStore(_temp.Root, options);
        await store.RememberAsync("task", "scope", "the deploy key rotates monthly");

        // a FILE where the scope's directory was, so every write under it fails on any platform
        var scope = Path.GetDirectoryName(Only("memory"))!;
        Directory.Delete(scope, recursive: true);
        File.WriteAllText(scope, "");

        Assert.Equal(["the deploy key rotates monthly"],
            (await store.RecallAsync("task", query: "deploy")).Select(m => m.Content));
    }
}
