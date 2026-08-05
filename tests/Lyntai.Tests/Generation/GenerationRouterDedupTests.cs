using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The generation router deduplicates its candidate list, the way the LLM router always has — and it
/// does it on the RESOLVED (backend, model) pair, before the count that
/// <see cref="GenerationRoutingPolicy.ExemptSoleCandidate"/> reads is taken.
///
/// <para>Both halves matter, and the second is the sharper one. A repeated entry re-attempting a backend that
/// just failed is merely wasteful; a repeated entry making the SOLE capable backend look like two silently
/// withdraws the sole-candidate exemption, which turns an actionable "rate limited, try in a minute" into a
/// synthetic "no capable backend" — the exact harm the exemption exists to prevent. A dedup applied after the
/// count is taken fixes only the first.</para></summary>
public class GenerationRouterDedupTests
{
    private static GenerationRequest Image() => new() { Kind = GenerationKinds.Image, Prompt = "a red square" };

    private static DeadHostTracker Benching() => new(threshold: 1, cooldown: TimeSpan.FromMinutes(5));

    [Fact]
    public async Task A_repeated_candidate_is_attempted_ONCE_not_twice()
    {
        // a list that re-prepends the primary is a configuration mistake, not an instruction to retry
        var failing = new FakeGenerationProvider { Id = "a" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await new GenerationRouter([failing, working]).GenerateAsync(
            [new GenerationCandidate("a"), new GenerationCandidate("a"), new GenerationCandidate("b")], Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, failing.GenerateCalls);
        Assert.Equal(1, working.GenerateCalls);
    }

    [Fact]
    public async Task Two_entries_naming_one_backend_do_not_withdraw_the_sole_candidate_exemption()
    {
        // ONE backend listed twice is still one backend. Counting it as two benches the only option there is,
        // and the caller is told "everything is on cooldown" instead of the rate limit it could act on.
        var sole = new FakeGenerationProvider { Id = "sole" };
        sole.Verdicts.Enqueue(GenerationVerdict.RateLimited);
        var router = new GenerationRouter([sole], deadHosts: Benching());
        GenerationCandidate[] listedTwice = [new("sole"), new("sole")];

        await router.GenerateAsync(listedTwice, Image());
        var second = await router.GenerateAsync(listedTwice, Image());

        Assert.Equal(2, sole.GenerateCalls);                       // still asked: it is the only candidate
        Assert.Equal(GenerationVerdict.RateLimited, second.Verdict);
        Assert.DoesNotContain("cooldown", second.Detail);          // a real verdict, not a fabricated one
    }

    [Fact]
    public async Task Ids_differing_only_in_case_are_ONE_candidate_because_they_resolve_to_one_backend()
    {
        // the router already resolves ids case-insensitively, so "a1111" and "A1111" select the same instance —
        // deduping on the SPEC rather than on what it resolved to would let this pair through as two
        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        var working = new FakeGenerationProvider { Id = "local" };

        var result = await new GenerationRouter([failing, working]).GenerateAsync(
            [new GenerationCandidate("a1111"), new GenerationCandidate("A1111"), new GenerationCandidate("local")],
            Image());

        Assert.True(result.IsOk);
        Assert.Equal(1, failing.GenerateCalls);
    }

    [Fact]
    public async Task A_candidate_pinning_the_model_the_request_already_names_is_the_same_candidate()
    {
        // the other half of "resolved": the candidate's model is applied to the request, so pinning the model
        // the request already carries produces byte-for-byte the same call as pinning nothing
        var aggregator = Aggregator("aggregator");
        aggregator.Verdicts.Enqueue(GenerationVerdict.Failed);
        var working = new FakeGenerationProvider { Id = "b" };

        var result = await new GenerationRouter([aggregator, working]).GenerateAsync(
            [new GenerationCandidate("aggregator", "sdxl"), new GenerationCandidate("aggregator"),
             new GenerationCandidate("b")],
            Image() with { Model = "sdxl" });

        Assert.True(result.IsOk);
        Assert.Equal(1, aggregator.GenerateCalls);
    }

    [Fact]
    public async Task Two_DIFFERENT_models_at_one_backend_stay_two_candidates()
    {
        // the counterweight, and the reason the key is a PAIR: an aggregator serves hundreds of models behind
        // one id, and falling over from one of them to another is exactly what a fallback list is for
        var aggregator = Aggregator("aggregator");
        aggregator.Verdicts.Enqueue(GenerationVerdict.Failed);
        aggregator.Verdicts.Enqueue(GenerationVerdict.Ok);

        var result = await new GenerationRouter([aggregator]).GenerateAsync(
            [new GenerationCandidate("aggregator", "flux-1"), new GenerationCandidate("aggregator", "sdxl")],
            Image());

        Assert.True(result.IsOk);
        Assert.Equal(2, aggregator.GenerateCalls);
    }

    private static FakeGenerationProvider Aggregator(string id) => new()
    {
        Id = id,
        Capabilities = new GenerationCapabilities
        {
            Kinds = [GenerationKinds.Image],
            Deliveries = [GenerationDelivery.Inline],
            Models = ["flux-1", "sdxl"],
        },
    };
}
