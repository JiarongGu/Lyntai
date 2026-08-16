using Lyntai;
using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>Media backends are a DI COLLECTION, like providers and scorers — adding one is a registration,
/// never an edit to a switch (<c>dotnet-package-layout.md</c>). The router resolves the collection.</summary>
public class GenerationDiTests
{
    [Fact]
    public void Registered_media_backends_are_resolved_as_a_collection()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a" })
            .AddGenerationProvider(_ => new FakeGenerationJobProvider { Id = "b" }));
        using var sp = services.BuildServiceProvider();

        var ids = sp.GetServices<IGenerationProvider>().Select(p => p.Id).ToList();

        Assert.Contains("a", ids);
        Assert.Contains("b", ids);
    }

    [Fact]
    public void The_router_is_registered_and_sees_every_backend()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg.AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a" }));
        using var sp = services.BuildServiceProvider();

        Assert.NotNull(sp.GetRequiredService<IGenerationRouter>());
    }

    /// <summary><b>A pre-registered <see cref="GenerationOptions"/> is REUSED, not replaced</b> — the
    /// documented way to set a knob the builder exposes no dedicated method for, and the same DI-registration
    /// path <c>SalienceOptions</c> and <c>DsrOptions</c> take. Asserted because the doc on
    /// <see cref="GenerationOptions.ProbeDeadline"/> tells a consumer to do exactly this, and it holds only
    /// while the builder's get-or-register keeps preferring an existing instance.
    /// <para>The second half is what makes it worth a test rather than a comment: the builder's OWN
    /// configuration must land on that same object, or a consumer who sets one knob this way silently loses
    /// the others.</para></summary>
    [Fact]
    public void A_pre_registered_GenerationOptions_is_reused_so_both_sides_configure_one_instance()
    {
        var mine = new GenerationOptions { ProbeDeadline = TimeSpan.FromSeconds(3) };
        var services = new ServiceCollection();
        services.AddSingleton(mine);
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a" })
            .UseDefaultGenerationCandidates("a"));
        using var sp = services.BuildServiceProvider();

        var resolved = sp.GetRequiredService<GenerationOptions>();

        Assert.Same(mine, resolved);
        Assert.Equal(TimeSpan.FromSeconds(3), resolved.ProbeDeadline);   // the consumer's knob survived
        Assert.Single(resolved.DefaultCandidates);                       // and the builder's landed on it
    }

    [Fact]
    public async Task Default_candidates_are_used_when_a_caller_names_none()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a" })
            .UseDefaultGenerationCandidates("a"));
        using var sp = services.BuildServiceProvider();

        var options = sp.GetRequiredService<GenerationOptions>();
        var router = sp.GetRequiredService<IGenerationRouter>();

        var result = await router.GenerateAsync(options.DefaultCandidates,
            new GenerationRequest { Kind = GenerationKinds.Image, Prompt = "x" });

        Assert.True(result.IsOk);
        Assert.Equal(["a"], options.DefaultCandidates.Select(c => c.ProviderId));
    }

    [Fact]
    public void A_candidate_can_name_a_model_inline()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "aggregator" })
            .UseDefaultGenerationCandidates("aggregator:sdxl"));
        using var sp = services.BuildServiceProvider();

        var candidate = sp.GetRequiredService<GenerationOptions>().DefaultCandidates.Single();

        Assert.Equal("aggregator", candidate.ProviderId);
        Assert.Equal("sdxl", candidate.Model);
    }

    [Fact]
    public void Setting_default_candidates_REPLACES_rather_than_appends()
    {
        // matches LyntaiBuilder.UseDefaultCandidates exactly — the last call wins, so the two domains behave
        // the same way and a re-configuration can't silently accumulate a stale backend
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "a" })
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "b" })
            .UseDefaultGenerationCandidates("a")
            .UseDefaultGenerationCandidates("b"));
        using var sp = services.BuildServiceProvider();

        Assert.Equal(["b"], sp.GetRequiredService<GenerationOptions>().DefaultCandidates.Select(c => c.ProviderId));
    }
}
