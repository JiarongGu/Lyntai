using Lyntai.Storage;
using Lyntai.Storage.FileSystem;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Storage.FileSystem;

/// <summary><c>UseFileSystemStorage</c>: what it registers, what it leaves for another backend, and that a
/// container owns its root exactly as long as it lives.</summary>
public class FileSystemWiringTests : IDisposable
{
    private readonly string _dir = Path.Combine(TestPaths.TestScratchDir, $"fs-wiring-{Guid.NewGuid():N}");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* gitignored scratch */ }
    }

    private ServiceProvider Build(Action<LyntaiBuilder>? more = null)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b =>
        {
            b.UseFileSystemStorage(o => o.Root = _dir);
            more?.Invoke(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void A_root_is_required_rather_than_guessed() =>
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLyntai(b => b.UseFileSystemStorage(_ => { })));

    [Fact]
    public async Task It_serves_the_domains_a_person_reads_and_leaves_the_rest_unresolvable()
    {
        using var sp = Build();

        Assert.IsType<FileSystemKeyValueStore>(sp.GetRequiredService<IKeyValueStore>());
        Assert.IsType<FileSystemPromptVersionStore>(sp.GetRequiredService<IPromptVersionStore>());
        Assert.IsType<FileSystemConversationStore>(sp.GetRequiredService<IConversationStore>());
        Assert.IsType<FileSystemMemoryStore>(sp.GetRequiredService<IMemoryStore>());
        Assert.IsType<FileSystemCuratedMemoryStore>(sp.GetRequiredService<ICuratedMemoryStore>());
        Assert.Null(sp.GetService<IJobStore>());
        Assert.Null(sp.GetService<IScoreStore>());
        Assert.IsType<FileSystemMemoryGraphStore>(sp.GetRequiredService<Lyntai.Memory.IMemoryGraphStore>());

        await sp.GetRequiredService<IKeyValueStore>().SetAsync("k", "v");
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "kv")));
    }

    [Fact]
    public async Task A_memory_engine_over_file_storage_alone_remembers_and_recalls_across_a_restart()
    {
        ServiceProvider Engine() => Build(b => b
            .AddProvider(_ => new Lyntai.Tests.Fakes.FakeTextProvider("p"))
            .AddMemoryEngine("notes", e => e.UseGraph()));

        using (var first = Engine())
            await first.GetRequiredService<Lyntai.Memory.IMemoryEngineFactory>().Get("notes/graph")
                .RememberAsync(new Lyntai.Memory.MemoryWrite("assistant", "user", "the deploy key rotates monthly"));

        using var second = Engine();
        var recall = await second.GetRequiredService<Lyntai.Memory.IMemoryEngineFactory>().Get("notes/graph")
            .RecallAsync(new Lyntai.Memory.MemoryQuery("assistant", Query: "deploy"));

        Assert.Equal(["the deploy key rotates monthly"], recall.Items.Select(i => i.Content ?? i.Headline));
        Assert.Single(Directory.GetFiles(Path.Combine(_dir, "graph"), "*.md", SearchOption.AllDirectories));
    }

    [Fact]
    public void Registered_first_it_takes_its_domains_and_SQLite_fills_the_rest()
    {
        using var db = new TempDbPath("fs-compose");
        using var sp = Build(b => b.UseSqliteStorage(db.Path));

        Assert.IsType<FileSystemKeyValueStore>(sp.GetRequiredService<IKeyValueStore>());
        Assert.IsType<FileSystemCuratedMemoryStore>(sp.GetRequiredService<ICuratedMemoryStore>());
        Assert.IsType<Lyntai.Storage.Sqlite.SqliteJobStore>(sp.GetRequiredService<IJobStore>());
    }

    [Fact]
    public async Task A_container_owns_its_root_until_it_is_disposed()
    {
        var first = Build();
        await first.GetRequiredService<IKeyValueStore>().SetAsync("k", "v");

        using (var second = Build())
            Assert.Throws<InvalidOperationException>(() => second.GetRequiredService<IKeyValueStore>());

        await first.DisposeAsync();
        using var third = Build();
        Assert.Equal("v", await third.GetRequiredService<IKeyValueStore>().GetAsync("k"));
    }
}
