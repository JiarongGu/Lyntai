using Lyntai.Cortex;
using Lyntai.Memory;
using Lyntai.Storage;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Memory;

/// <summary>The registration seam: several named engines coexist in one container, are addressed by name,
/// and a mistake in the wiring surfaces at STARTUP rather than as a permanently empty memory section.</summary>
public class MemoryEngineRegistrationTests
{
    private static ServiceProvider Build(Action<LyntaiBuilder> configure)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddLyntai(b =>
        {
            b.AddProvider(_ => new FakeLlmProvider("p"));
            configure(b);
        });
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddMemory_registers_one_working_engine_with_no_configuration()
    {
        using var sp = Build(cfg => cfg.AddMemory());

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get();

        Assert.Equal("default", engine.Name);
    }

    [Fact]
    public void Engines_are_addressable_by_name_and_several_coexist()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("chat", e => e.UseLexical())
            .AddMemoryEngine("project", e => e.UseLexical()));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();

        Assert.Equal("chat", factory.Get("chat").Name);
        Assert.Equal("project", factory.Get("project").Name);
    }

    [Fact]
    public void A_single_engine_under_any_name_is_the_default_the_parameterless_Get_returns()
    {
        // The interface doc: "the one named 'default', or the ONLY one when exactly one is registered."
        // The only-one fallback counted index ENTRIES, which include a composite's members — so the one
        // path that reaches it through the public API (a sole engine not named "default", whose lexical
        // member makes the index 2) threw "2 are registered" at the consumer who registered exactly one.
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseLexical()));

        var engine = sp.GetRequiredService<IMemoryEngineFactory>().Get();

        Assert.Equal("project", engine.Name);
    }

    [Fact]
    public void Two_engines_and_no_default_still_refuse_the_parameterless_Get_by_the_TOP_LEVEL_count()
    {
        // The refusal must count ENGINES, not index entries: two engines carry four index entries, and a
        // message saying "4 are registered" sends the consumer hunting for engines that do not exist.
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("chat", e => e.UseLexical())
            .AddMemoryEngine("project", e => e.UseLexical()));

        var ex = Assert.Throws<KeyNotFoundException>(() =>
            sp.GetRequiredService<IMemoryEngineFactory>().Get());

        Assert.Contains("2 are registered", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Members_are_addressable_by_their_hierarchical_name()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseLexical()));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();

        Assert.True(factory.TryGet("project/lexical", out var member));
        Assert.Equal("project/lexical", member.Name);
    }

    [Fact]
    public void An_unknown_name_throws_and_lists_what_is_registered()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("chat", e => e.UseLexical()));

        var ex = Assert.Throws<KeyNotFoundException>(() =>
            sp.GetRequiredService<IMemoryEngineFactory>().Get("nope"));

        Assert.Contains("chat", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_engines_with_the_same_name_fail_at_configure_time()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(cfg => cfg
            .AddMemoryEngine("chat", e => e.UseLexical())
            .AddMemoryEngine("chat", e => e.UseLexical())));

        Assert.Contains("chat", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_unlabelled_members_of_the_same_kind_fail_at_configure_time()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(cfg => cfg
            .AddMemoryEngine("project", e => e.UseLexical().UseLexical())));

        Assert.Contains("project/lexical", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_curated_members_are_named_by_their_catalog_kind_and_do_not_collide()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<ICuratedMemoryStore>(new FakeCuratedStore());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e.UseCurated("glossary").UseCurated("style")));
        using var sp = services.BuildServiceProvider();

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();

        Assert.True(factory.TryGet("project/glossary", out _));
        Assert.True(factory.TryGet("project/style", out _));
    }

    [Fact]
    public void Labelled_members_of_the_same_kind_are_fine()
    {
        using var sp = Build(cfg => cfg.AddMemoryEngine("project",
            e => e.UseLexical().UseLexical(label: "second")));

        var factory = sp.GetRequiredService<IMemoryEngineFactory>();

        Assert.True(factory.TryGet("project/lexical", out _));
        Assert.True(factory.TryGet("project/second", out _));
    }

    [Fact]
    public void A_member_whose_backing_store_is_absent_fails_at_startup_naming_the_store()
    {
        // no ICuratedMemoryStore registered — this must NOT resolve to a permanently empty section that
        // reads exactly like "nothing matched"
        using var sp = Build(cfg => cfg.AddMemoryEngine("project", e => e.UseCurated()));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            sp.GetRequiredService<IMemoryEngineFactory>());

        Assert.Contains("ICuratedMemoryStore", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseMemoryComposer_backs_the_prompt_composer_with_the_named_engine()
    {
        using var sp = Build(cfg => cfg
            .AddMemoryEngine("chat", e => e.UseLexical())
            .UseMemoryComposer("chat"));

        var store = sp.GetRequiredService<IMemoryStore>();
        await store.RememberAsync("t", "s", "the composer reads this engine");

        var composed = await sp.GetRequiredService<IPromptComposer>()
            .ComposeAsync("BASE", "t", "s", "composer");

        Assert.Contains("the composer reads this engine", composed, StringComparison.Ordinal);
    }

    [Fact]
    public void Registering_no_engine_leaves_the_existing_composer_in_place()
    {
        using var sp = Build(_ => { });

        Assert.IsType<MemoryPromptComposer>(sp.GetRequiredService<IPromptComposer>());
    }

    [Fact]
    public async Task A_blend_reserves_budget_for_its_authoritative_member()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMemoryStore>(new FakeMemoryStore());
        services.AddSingleton<ICuratedMemoryStore>(new FakeCuratedStore());
        services.AddLyntai(b => b
            .AddProvider(_ => new FakeLlmProvider("p"))
            .AddMemoryEngine("project", e => e
                .UseCurated("glossary").ReserveCharacters(200)
                .UseLexical()
                .Budget(400))
            .UseMemoryComposer("project"));
        using var sp = services.BuildServiceProvider();

        var glossary = sp.GetRequiredService<IMemoryEngineFactory>().Get("project/glossary");
        await glossary.RememberAsync(new MemoryWrite("t", "s", "the build gate is dev.mjs verify",
            Grade: MemoryGrade.Authoritative));

        var lexical = sp.GetRequiredService<IMemoryStore>();
        for (var i = 0; i < 100; i++)
            await lexical.RememberAsync("t", "s", $"gate related noise number {i} at some length");

        var composed = await sp.GetRequiredService<IPromptComposer>()
            .ComposeAsync("BASE", "t", "s", "gate");

        Assert.Contains("the build gate is dev.mjs verify", composed, StringComparison.Ordinal);
        Assert.Contains("## Known facts (authoritative)", composed, StringComparison.Ordinal);
    }
}
