using Lyntai.Memory;
using Lyntai.Memory.Engines;
using Lyntai.Storage.InMemory;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>A read-only member — <c>UseCurated(kind: null)</c>, which reads every catalog section and has none
/// to write into — must say so in <see cref="IMemoryEngine.Supported"/>, and a blend must route AROUND it.</summary>
public class ReadOnlyMemberRoutingTests
{
    private static ServiceProvider Build(Action<MemoryEngineBuilder> engine)
    {
        var services = new ServiceCollection();
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeTextProvider("p"))
            .UseInMemoryStorage()
            .AddMemoryEngine("project", engine));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void An_every_section_curated_member_supports_no_grade()
    {
        var engine = new CuratedMemoryEngine("c", new InMemoryCuratedMemoryStore(), kind: null);

        Assert.Equal(MemoryGrades.None, engine.Supported);
    }

    [Fact]
    public async Task A_blend_leading_with_a_read_only_member_stores_an_Inherit_write_in_the_next()
    {
        await using var sp = Build(e => e.UseCurated(kind: null).UseGraph());
        var project = sp.GetRequiredService<IMemoryEngineFactory>().Get("project");

        var written = await project.RememberAsync(new MemoryWrite("t", "s", "the deploy runs on fridays"));

        Assert.Equal("project/graph", written.Reference.Engine);
    }

    [Fact]
    public async Task A_blend_leading_with_a_read_only_member_stores_an_authoritative_write_in_the_graph()
    {
        await using var sp = Build(e => e.UseCurated(kind: null).UseGraph().FanOutWrites());
        var project = sp.GetRequiredService<IMemoryEngineFactory>().Get("project");

        var written = await project.RememberAsync(
            new MemoryWrite("t", "s", "the key rotates monthly", Grade: MemoryGrade.Authoritative));

        Assert.Equal("project/graph", written.Reference.Engine);
    }

    /// <summary>A BYO engine that declares itself read-only.</summary>
    private sealed class ReadOnlyEngine : IMemoryEngine
    {
        public string Name => "ro";
        public MemoryGrades Supported => MemoryGrades.None;
        public Task<MemoryWriteResult> RememberAsync(MemoryWrite write, CancellationToken ct = default) =>
            throw new NotSupportedException();
        public Task<MemoryRecall> RecallAsync(MemoryQuery query, CancellationToken ct = default) =>
            Task.FromResult(MemoryRecall.Empty);
    }

    [Fact]
    public void The_default_removal_policy_leaves_a_read_only_member_out_of_scope()
    {
        var policy = new DefaultMemoryRemovalPolicy();

        Assert.False(policy.Includes(new ReadOnlyEngine(), MemoryRemovalKind.Forget));
        Assert.False(policy.Includes(new ReadOnlyEngine(), MemoryRemovalKind.Prune));
    }

    [Fact]
    public async Task A_blend_with_a_read_only_member_can_still_forget()
    {
        await using var sp = Build(e => e.UseCurated(kind: null).UseGraph());
        var project = sp.GetRequiredService<IMemoryEngineFactory>().Get("project");
        await project.RememberAsync(new MemoryWrite("t", "s", "a user's own words"));

        await ((IForgettableMemory)project).ForgetAsync("t", "s");

        Assert.Empty((await project.RecallAsync(new MemoryQuery("t", "s", "words"))).Items);
    }
}
