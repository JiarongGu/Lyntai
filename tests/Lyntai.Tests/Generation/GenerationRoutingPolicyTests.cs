using Lyntai;
using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>Per-verdict fallback is a POLICY, not a law — the same lesson the LLM router learned in
/// <c>docs/DECISIONS.md</c> D10. The motivating case is real: a cloud backend refuses content that a
/// permissive local backend will happily produce, and the host — not the library — decides whether a refusal
/// ends the run or moves to the next candidate.</summary>
public class GenerationRoutingPolicyTests
{
    private static GenerationRequest Image() => new() { Kind = GenerationKinds.Image, Prompt = "x" };

    [Fact]
    public void By_default_a_refusal_surfaces_and_everything_else_advances()
    {
        var policy = new GenerationRoutingPolicy();

        Assert.Equal(GenerationFallbackAction.Surface, policy.ActionFor(GenerationVerdict.Refused));
        Assert.Equal(GenerationFallbackAction.Advance, policy.ActionFor(GenerationVerdict.Failed));
        Assert.Equal(GenerationFallbackAction.Advance, policy.ActionFor(GenerationVerdict.RateLimited));
        Assert.Equal(GenerationFallbackAction.Advance, policy.ActionFor(GenerationVerdict.NotConfigured));
        Assert.Equal(GenerationFallbackAction.Advance, policy.ActionFor(GenerationVerdict.Unsupported));
    }

    [Fact]
    public async Task A_permissive_backend_can_pick_up_what_another_refused_when_configured_to()
    {
        // the case that forced this seam: a hosted backend refuses on content policy, a locally-run one has no
        // such policy, and the host has deliberately listed both
        var refusing = new FakeGenerationProvider { Id = "hosted" };
        refusing.Verdicts.Enqueue(GenerationVerdict.Refused);
        var permissive = new FakeGenerationProvider { Id = "local" };
        var policy = new GenerationRoutingPolicy().On(GenerationVerdict.Refused, GenerationFallbackAction.Advance);
        var router = new GenerationRouter([refusing, permissive], policy);

        var result = await router.GenerateAsync(
            [new GenerationCandidate("hosted"), new GenerationCandidate("local")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, permissive.GenerateCalls);
    }

    [Fact]
    public async Task When_every_backend_refuses_the_refusal_is_still_what_gets_reported()
    {
        // advancing past a refusal must not lose it: if nothing succeeds, the caller needs to know it was
        // refused rather than "not configured"
        var first = new FakeGenerationProvider { Id = "a" };
        first.Verdicts.Enqueue(GenerationVerdict.Refused);
        var second = new FakeGenerationProvider { Id = "b" };
        second.Verdicts.Enqueue(GenerationVerdict.Refused);
        var policy = new GenerationRoutingPolicy().On(GenerationVerdict.Refused, GenerationFallbackAction.Advance);
        var router = new GenerationRouter([first, second], policy);

        var result = await router.GenerateAsync(
            [new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.Equal(GenerationVerdict.Refused, result.Verdict);
        Assert.Equal(1, second.GenerateCalls);   // it did try the second one
    }

    [Fact]
    public async Task A_verdict_can_be_made_to_SURFACE_instead_of_advancing()
    {
        // the reverse knob: a host that would rather see a hard failure than silently pay a second backend
        var failing = new FakeGenerationProvider { Id = "a" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        var working = new FakeGenerationProvider { Id = "b" };
        var policy = new GenerationRoutingPolicy().On(GenerationVerdict.Failed, GenerationFallbackAction.Surface);
        var router = new GenerationRouter([failing, working], policy);

        var result = await router.GenerateAsync(
            [new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.Equal(GenerationVerdict.Failed, result.Verdict);
        Assert.Equal(0, working.GenerateCalls);
    }

    [Fact]
    public async Task The_policy_is_configurable_through_DI()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ =>
            {
                var refusing = new FakeGenerationProvider { Id = "hosted" };
                refusing.Verdicts.Enqueue(GenerationVerdict.Refused);
                return refusing;
            })
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "local" })
            .ConfigureGenerationRouting(p => p.On(GenerationVerdict.Refused, GenerationFallbackAction.Advance)));
        using var sp = services.BuildServiceProvider();

        var result = await sp.GetRequiredService<IGenerationRouter>().GenerateAsync(
            [new GenerationCandidate("hosted"), new GenerationCandidate("local")], Image());

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task The_default_policy_applies_when_none_is_configured()
    {
        // a router built without a policy must behave exactly as before this seam existed
        var refusing = new FakeGenerationProvider { Id = "a" };
        refusing.Verdicts.Enqueue(GenerationVerdict.Refused);
        var working = new FakeGenerationProvider { Id = "b" };
        var router = new GenerationRouter([refusing, working]);

        var result = await router.GenerateAsync(
            [new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.Equal(GenerationVerdict.Refused, result.Verdict);
        Assert.Equal(0, working.GenerateCalls);
    }
}
