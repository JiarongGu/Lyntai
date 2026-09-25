using Lyntai.Inference;
using Lyntai;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>Per-verdict fallback is a POLICY, not a law — the same lesson the LLM router learned in
/// <c>docs/DECISIONS.md</c> D3. The motivating case is real: a cloud backend refuses content that a
/// permissive local backend will happily produce, and the host — not the library — decides whether a refusal
/// ends the run or moves to the next candidate.</summary>
public class MediaRoutingPolicyTests
{
    private static MediaRequest Image() => new() { Kind = ProviderKinds.Image, Prompt = "x" };

    [Fact]
    public void The_defaults_mirror_the_LLM_router_so_one_mental_model_covers_both_domains()
    {
        var policy = new MediaRoutingPolicy();

        // a content judgement ends the run; a capability gap is nobody's fault; a backend that told us to
        // back off gets benched; a maybe-transient fault counts toward the threshold
        Assert.Equal(FallbackAction.Surface, policy.ActionFor(ProviderVerdict.Refused));
        Assert.Equal(FallbackAction.Advance, policy.ActionFor(ProviderVerdict.NotConfigured));
        Assert.Equal(FallbackAction.Advance, policy.ActionFor(ProviderVerdict.Unsupported));
        // an oversized prompt is the request's fault, so it must not bench a healthy backend either
        Assert.Equal(FallbackAction.Advance, policy.ActionFor(ProviderVerdict.ContextWindowExceeded));
        Assert.Equal(FallbackAction.CooldownAndAdvance, policy.ActionFor(ProviderVerdict.RateLimited));
        Assert.Equal(FallbackAction.CooldownAndAdvance, policy.ActionFor(ProviderVerdict.AuthFailed));
        Assert.Equal(FallbackAction.PenalizeAndAdvance, policy.ActionFor(ProviderVerdict.Failed));
        Assert.Equal(FallbackAction.PenalizeAndAdvance, policy.ActionFor(ProviderVerdict.Timeout));
    }

    [Fact]
    public async Task A_permissive_backend_can_pick_up_what_another_refused_when_configured_to()
    {
        // the case that forced this seam: a hosted backend refuses on content policy, a locally-run one has no
        // such policy, and the host has deliberately listed both
        var refusing = new FakeGenerationProvider { Id = "hosted" };
        refusing.Verdicts.Enqueue(ProviderVerdict.Refused);
        var permissive = new FakeGenerationProvider { Id = "local" };
        var policy = new MediaRoutingPolicy().On(ProviderVerdict.Refused, FallbackAction.Advance);
        var router = new MediaRouter([refusing, permissive], policy);

        var result = await router.GenerateAsync(
            [new ProviderCandidate("hosted"), new ProviderCandidate("local")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, permissive.GenerateCalls);
    }

    [Fact]
    public async Task When_every_backend_refuses_the_refusal_is_still_what_gets_reported()
    {
        // advancing past a refusal must not lose it: if nothing succeeds, the caller needs to know it was
        // refused rather than "not configured"
        var first = new FakeGenerationProvider { Id = "a" };
        first.Verdicts.Enqueue(ProviderVerdict.Refused);
        var second = new FakeGenerationProvider { Id = "b" };
        second.Verdicts.Enqueue(ProviderVerdict.Refused);
        var policy = new MediaRoutingPolicy().On(ProviderVerdict.Refused, FallbackAction.Advance);
        var router = new MediaRouter([first, second], policy);

        var result = await router.GenerateAsync(
            [new ProviderCandidate("a"), new ProviderCandidate("b")], Image());

        Assert.Equal(ProviderVerdict.Refused, result.Verdict);
        Assert.Equal(1, second.GenerateCalls);   // it did try the second one
    }

    [Fact]
    public async Task A_verdict_can_be_made_to_SURFACE_instead_of_advancing()
    {
        // the reverse knob: a host that would rather see a hard failure than silently pay a second backend
        var failing = new FakeGenerationProvider { Id = "a" };
        failing.Verdicts.Enqueue(ProviderVerdict.Failed);
        var working = new FakeGenerationProvider { Id = "b" };
        var policy = new MediaRoutingPolicy().On(ProviderVerdict.Failed, FallbackAction.Surface);
        var router = new MediaRouter([failing, working], policy);

        var result = await router.GenerateAsync(
            [new ProviderCandidate("a"), new ProviderCandidate("b")], Image());

        Assert.Equal(ProviderVerdict.Failed, result.Verdict);
        Assert.Equal(0, working.GenerateCalls);
    }

    [Fact]
    public async Task The_policy_is_configurable_through_DI()
    {
        var services = new ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddProvider(_ =>
            {
                var refusing = new FakeGenerationProvider { Id = "hosted" };
                refusing.Verdicts.Enqueue(ProviderVerdict.Refused);
                return refusing;
            })
            .AddProvider(_ => new FakeGenerationProvider { Id = "local" })
            .ConfigureMediaRouting(p => p.On(ProviderVerdict.Refused, FallbackAction.Advance))
            .AddMediaRouting());
        using var sp = services.BuildServiceProvider();

        var result = await sp.GetRequiredService<IMediaRouter>().GenerateAsync(
            [new ProviderCandidate("hosted"), new ProviderCandidate("local")], Image());

        Assert.True(result.IsOk);
    }

}
