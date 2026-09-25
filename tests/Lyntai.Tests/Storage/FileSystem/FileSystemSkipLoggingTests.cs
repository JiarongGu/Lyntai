using Lyntai.Memory;
using Lyntai.Storage.FileSystem;
using Microsoft.Extensions.Logging;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary>The "logged" half of "skipped, logged, never written over": every file domain names what it skipped
/// in a warning a person can act on. Every other file-system suite runs on a null logger, so without these a
/// deleted warning would fail nothing.</summary>
public class FileSystemSkipLoggingTests : IDisposable
{
    private const string Broken = "a person's half-finished edit";
    private readonly CapturingLogger _log = new(minLevel: LogLevel.Trace);
    private readonly TempRoot _temp;

    public FileSystemSkipLoggingTests() => _temp = new TempRoot(_log);

    public void Dispose() => _temp.Dispose();

    private void AssertWarned(string phrase, string file) =>
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Warning
            && e.Message.Contains(phrase, StringComparison.Ordinal)
            && e.Message.Contains(Path.GetFileName(file), StringComparison.Ordinal));

    private string Only(string domain, string pattern = "*.md") =>
        Directory.GetFiles(Path.Combine(_temp.Directory, domain), pattern, SearchOption.AllDirectories).Single();

    // Overwrites the one file of that pattern while no process owns the root, then takes the root back.
    private string Break(string domain, string pattern = "*.md")
    {
        var file = Only(domain, pattern);
        _temp.Root.Dispose();
        File.WriteAllText(file, Broken);
        _temp.Reopen();
        return file;
    }

    private static GraphNodeWrite Memory(string content) =>
        new("e", "task", "s", "h", content, MemoryGrade.Associative, 3, 1, null);

    [Fact]
    public async Task A_key_file_that_does_not_parse_is_named_in_a_warning()
    {
        await new FileSystemKeyValueStore(_temp.Root).SetAsync("k", "v");
        var file = Break("kv");

        await new FileSystemKeyValueStore(_temp.Root).GetAsync("k");

        AssertWarned("not a readable record", file);
    }

    [Fact]
    public async Task A_prompt_revision_that_does_not_parse_is_named_in_a_warning()
    {
        await new FileSystemPromptVersionStore(_temp.Root).SaveAsync("greet", "v1");
        var file = Break("prompts", "v0001.md");

        await new FileSystemPromptVersionStore(_temp.Root).HistoryAsync("greet");

        AssertWarned("not a readable record", file);
    }

    [Fact]
    public async Task A_thread_that_does_not_parse_is_named_in_a_warning()
    {
        await new FileSystemConversationStore(_temp.Root).CreateThreadAsync("t1");
        var file = Break("conversations", "thread.md");

        await new FileSystemConversationStore(_temp.Root).GetThreadAsync("t1");

        AssertWarned("not a readable record", file);
    }

    [Fact]
    public async Task A_task_memory_that_does_not_parse_is_named_in_a_warning()
    {
        var options = new LyntaiOptions();
        await new FileSystemMemoryStore(_temp.Root, options).RememberAsync("task", "scope", "a fact");
        var file = Break("memory");

        await new FileSystemMemoryStore(_temp.Root, options).RecallAsync("task");

        AssertWarned("not a readable record", file);
    }

    [Fact]
    public async Task A_curated_entry_that_does_not_parse_is_named_in_a_warning()
    {
        await new FileSystemCuratedMemoryStore(_temp.Root).AddAsync("style", "use short sentences");
        var file = Break("curated");

        await new FileSystemCuratedMemoryStore(_temp.Root).ListAsync();

        AssertWarned("not a readable record", file);
    }

    [Fact]
    public async Task A_graph_memory_that_does_not_parse_is_named_in_a_warning()
    {
        await new FileSystemMemoryGraphStore(_temp.Root).UpsertAsync(Memory("alpha"));
        var file = Break("graph");

        await new FileSystemMemoryGraphStore(_temp.Root).GetAsync("e", 1);

        AssertWarned("not a readable record", file);
    }

    [Fact]
    public async Task A_graph_memory_with_no_journal_state_is_named_in_a_warning()
    {
        await new FileSystemMemoryGraphStore(_temp.Root).UpsertAsync(Memory("alpha"));
        var copy = Path.Combine(Path.GetDirectoryName(Only("graph"))!, "000050.md");
        _temp.Root.Dispose();
        File.WriteAllText(copy, File.ReadAllText(Only("graph")).Replace("id: 1\n", "id: 50\n", StringComparison.Ordinal));
        _temp.Reopen();

        await new FileSystemMemoryGraphStore(_temp.Root).GetAsync("e", 50);

        AssertWarned("holds no state", copy);
    }

    [Fact]
    public async Task An_unreadable_journal_line_and_a_set_aside_tail_are_named_in_warnings()
    {
        await new FileSystemMemoryGraphStore(_temp.Root).UpsertAsync(Memory("alpha"));
        var journal = Only("graph", "journal.jsonl");
        _temp.Root.Dispose();
        File.AppendAllText(journal, "a person's note\nand an unterminated one");
        _temp.Reopen();

        await new FileSystemMemoryGraphStore(_temp.Root).GetAsync("e", 1);

        AssertWarned("unreadable line", journal);
        AssertWarned("aside", journal + ".torn");
    }

    [Fact]
    public async Task A_refused_journal_rewrite_is_named_in_a_warning()
    {
        var store = new FileSystemMemoryGraphStore(_temp.Root, compactionFloor: 0);
        var id = await store.UpsertAsync(Memory("alpha"));
        var journal = Only("graph", "journal.jsonl");
        Directory.CreateDirectory(journal + ".tmp"); // the atomic rewrite's temporary file cannot be created

        for (var i = 0; i < 6; i++) await store.TouchAsync("e", [new Lyntai.Memory.GraphTouch(id, 3)]);

        AssertWarned("could not rewrite", journal);
    }
}
