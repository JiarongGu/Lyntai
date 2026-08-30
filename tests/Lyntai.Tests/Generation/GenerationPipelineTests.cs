using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Llm.Budgeting;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Generation;

/// <summary>The pipeline runner: ordered stages, each feeding the next through
/// <see cref="GenerationArtifact.ToInput"/>. Driven through a scripted <see cref="IGenerationRouter"/> so a
/// fact can stage the artifact counts a real backend produces — and, for the governance fact, through the
/// REAL router under the budget decorator.</summary>
public class GenerationPipelineTests
{
    private static readonly GenerationRequest Image =
        new() { Kind = GenerationKinds.Image, Prompt = "a red square" };

    private static readonly GenerationRequest Video =
        new() { Kind = GenerationKinds.Video, Prompt = "pan across it" };

    private static IReadOnlyList<GenerationCandidate> Order(params string[] ids) =>
        [.. ids.Select(id => new GenerationCandidate(id))];

    /// <summary>An Ok carrying <paramref name="artifacts"/> artifacts. Built through the CONSTRUCTOR rather
    /// than <see cref="GenerationResult.Success"/> so zero is expressible — that shape is what a BYO router
    /// can return, and the runner has to refuse it rather than chain nothing.</summary>
    private static GenerationResult Produced(int artifacts, GenerationUsage? usage = null) =>
        new(GenerationVerdict.Ok,
            [.. Enumerable.Range(0, artifacts)
                .Select(i => new GenerationArtifact("image/png", Uri: $"https://example.invalid/{i}.png"))],
            usage ?? new GenerationUsage(Count: artifacts));

    [Fact]
    public async Task A_single_stage_pipeline_is_one_router_call_and_reports_its_artifacts()
    {
        var router = new ScriptedRouter(Produced(1));

        var result = await router.RunPipelineAsync([new GenerationStage(Image, Order("fal"))]);

        Assert.True(result.IsOk);
        Assert.Null(result.FailedAt);
        Assert.Equal(1, router.Calls);
        Assert.Single(result.Artifacts);
        Assert.Equal(GenerationVerdict.Ok, result.Verdict);
    }

    [Fact]
    public async Task Each_stage_routes_through_its_OWN_candidates()
    {
        // per-stage candidates is the whole reason a pipeline is not two hard-coded calls: an image backend
        // and a video backend are rarely the same vendor
        var router = new ScriptedRouter(Produced(1), Produced(1));

        await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd", "openai")),
            new GenerationStage(Video, Order("fal")) { InputRole = GenerationInputRoles.FirstFrame },
        ]);

        Assert.Equal(["sd", "openai"], router.Candidates[0].Select(c => c.ProviderId));
        Assert.Equal(["fal"], router.Candidates[1].Select(c => c.ProviderId));
    }

    [Fact]
    public async Task A_later_stage_receives_the_previous_stages_artifact_in_the_role_it_declared()
    {
        var router = new ScriptedRouter(Produced(1), Produced(1));

        await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd")),
            new GenerationStage(Video, Order("fal")) { InputRole = GenerationInputRoles.FirstFrame },
        ]);

        var chained = Assert.Single(router.Requests[1].Inputs);
        Assert.Equal(GenerationInputRoles.FirstFrame, chained.Role);
        Assert.Equal("https://example.invalid/0.png", chained.Uri);
        Assert.Equal("image/png", chained.MediaType);
    }

    [Fact]
    public async Task A_chained_input_is_appended_so_the_stages_OWN_inputs_survive()
    {
        // replacing would silently drop a style reference the caller attached to the video stage
        var style = GenerationInput.Reference(new Uri("https://example.invalid/style.png"), "image/png");
        var router = new ScriptedRouter(Produced(1), Produced(1));

        await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd")),
            new GenerationStage(Video with { Inputs = [style] }, Order("fal"))
                { InputRole = GenerationInputRoles.FirstFrame },
        ]);

        Assert.Equal(2, router.Requests[1].Inputs.Count);
        Assert.Equal(GenerationInputRoles.Reference, router.Requests[1].Inputs[0].Role);   // the caller's, first
        Assert.Equal(GenerationInputRoles.FirstFrame, router.Requests[1].Inputs[1].Role);
    }

    [Fact]
    public async Task A_null_role_chains_and_leaves_the_interpretation_to_the_backend()
    {
        var router = new ScriptedRouter(Produced(1), Produced(1));

        await router.RunPipelineAsync(
            [new GenerationStage(Image, Order("sd")), new GenerationStage(Video, Order("fal"))]);

        Assert.Null(Assert.Single(router.Requests[1].Inputs).Role);
    }

    [Fact]
    public async Task A_stage_that_cannot_choose_among_several_artifacts_refuses_without_calling_a_backend()
    {
        // Part 124: a media type cannot be branched on and "the first image/*" picks a UV atlas, so a stage
        // that cannot identify a chainable artifact REFUSES rather than guessing at a billed render
        var router = new ScriptedRouter(Produced(4));

        var result = await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd")),
            new GenerationStage(Video, Order("fal")) { InputRole = GenerationInputRoles.FirstFrame },
        ]);

        Assert.Equal(1, result.FailedAt);
        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Contains("4", result.Detail);
        Assert.Contains(nameof(GenerationStage.SelectInput), result.Detail);
        Assert.Equal(1, router.Calls);            // the refusal never reached a backend
    }

    [Fact]
    public async Task A_stage_whose_predecessor_produced_nothing_refuses_rather_than_chaining_nothing()
    {
        // an Ok with no artifacts is a contract violation a BYO router can still ship; the runner must not
        // hand the next stage an empty input list and call it a chain
        var router = new ScriptedRouter(Produced(0));

        var result = await router.RunPipelineAsync(
            [new GenerationStage(Image, Order("sd")), new GenerationStage(Video, Order("fal"))]);

        Assert.Equal(1, result.FailedAt);
        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Equal(1, router.Calls);
    }

    [Fact]
    public async Task A_supplied_selector_decides_which_artifact_chains_forward()
    {
        var router = new ScriptedRouter(Produced(4), Produced(1));

        var result = await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd")),
            new GenerationStage(Video, Order("fal"))
            {
                InputRole = GenerationInputRoles.FirstFrame,
                SelectInput = previous => [previous.Artifacts[2]],
            },
        ]);

        Assert.True(result.IsOk);
        Assert.Equal("https://example.invalid/2.png", Assert.Single(router.Requests[1].Inputs).Uri);
    }

    [Fact]
    public async Task A_selector_that_chooses_nothing_refuses_rather_than_falling_back_to_the_default()
    {
        // the default is what runs when there is NO selector; a selector that returned nothing has spoken
        var router = new ScriptedRouter(Produced(1));

        var result = await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd")),
            new GenerationStage(Video, Order("fal")) { SelectInput = _ => [] },
        ]);

        Assert.Equal(1, result.FailedAt);
        Assert.Equal(GenerationVerdict.Unsupported, result.Verdict);
        Assert.Equal(1, router.Calls);
    }

    [Fact]
    public async Task A_failed_stage_ends_the_run_so_the_paid_output_before_it_is_never_regenerated()
    {
        // the plan's requirement, asserted as a CALL COUNT rather than left as a comment
        var router = new ScriptedRouter(
            Produced(1),
            GenerationResult.Failure(GenerationVerdict.Refused, "content policy"),
            Produced(1));

        var result = await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd")),
            new GenerationStage(Video, Order("fal")) { InputRole = GenerationInputRoles.FirstFrame },
            new GenerationStage(Video, Order("fal")) { InputRole = GenerationInputRoles.FirstFrame },
        ]);

        Assert.Equal(2, router.Calls);                      // stage 3 never ran, stage 1 never re-ran
        Assert.Equal(1, result.FailedAt);
        Assert.Equal(GenerationVerdict.Refused, result.Verdict);
        Assert.Equal("content policy", result.Detail);
    }

    [Fact]
    public async Task The_artifacts_of_a_stage_that_succeeded_survive_a_later_stages_failure()
    {
        // stage 1's render is already billed; throwing it away is destroying something the caller paid for
        var router = new ScriptedRouter(
            Produced(1), GenerationResult.Failure(GenerationVerdict.Timeout, "too slow"));

        var result = await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd")),
            new GenerationStage(Video, Order("fal")) { InputRole = GenerationInputRoles.FirstFrame },
        ]);

        Assert.False(result.IsOk);
        Assert.Empty(result.Artifacts);                                       // the RUN produced nothing
        Assert.Equal("https://example.invalid/0.png",
            Assert.Single(result.Stages[0].Artifacts).Uri);                   // but stage 1's is still here
        Assert.Equal(2, result.Stages.Count);                                 // including the failed stage
    }

    [Fact]
    public async Task Usage_sums_across_the_stages_that_ran_and_stays_null_for_what_none_of_them_measured()
    {
        var router = new ScriptedRouter(
            Produced(1, new GenerationUsage(Count: 1, CostUsd: 0.02)),
            Produced(1, new GenerationUsage(Count: 1, Seconds: 5, CostUsd: 0.40)));

        var result = await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd")),
            new GenerationStage(Video, Order("fal")) { InputRole = GenerationInputRoles.FirstFrame },
        ]);

        Assert.Equal(2, result.Usage!.Count);
        Assert.Equal(5, result.Usage.Seconds);            // only stage 2 reported seconds
        Assert.Equal(0.42, result.Usage.CostUsd!.Value, 6);
    }

    [Fact]
    public async Task Usage_a_backend_never_reported_is_null_rather_than_an_invented_zero()
    {
        var router = new ScriptedRouter(new GenerationResult(GenerationVerdict.Ok,
            [new GenerationArtifact("image/png", Uri: "https://example.invalid/0.png")]));

        var result = await router.RunPipelineAsync([new GenerationStage(Image, Order("sd"))]);

        Assert.Null(result.Usage);
    }

    [Fact]
    public async Task The_callers_own_request_is_never_mutated()
    {
        var video = Video;
        var router = new ScriptedRouter(Produced(1), Produced(1));

        await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("sd")),
            new GenerationStage(video, Order("fal")) { InputRole = GenerationInputRoles.FirstFrame },
        ]);

        Assert.Empty(video.Inputs);                       // the chained input went to a COPY
        Assert.Single(router.Requests[1].Inputs);
    }

    [Fact]
    public async Task Cancellation_propagates_rather_than_being_reported_as_a_verdict()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var router = new ScriptedRouter(Produced(1));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            router.RunPipelineAsync([new GenerationStage(Image, Order("sd"))], cts.Token));

        Assert.Equal(0, router.Calls);
    }

    [Fact]
    public async Task An_empty_pipeline_throws_rather_than_reporting_a_successful_nothing()
    {
        var router = new ScriptedRouter();

        await Assert.ThrowsAsync<ArgumentException>(() => router.RunPipelineAsync([]));
    }

    [Fact]
    public async Task A_first_stage_that_declares_chaining_settings_throws_because_nothing_would_read_them()
    {
        // a setting nothing implements is the shape Part 125 fixed on ComfyUiProvider: the caller believes
        // something is happening, and silence is the expensive answer
        var router = new ScriptedRouter(Produced(1));

        await Assert.ThrowsAsync<ArgumentException>(() => router.RunPipelineAsync(
            [new GenerationStage(Image, Order("sd")) { InputRole = GenerationInputRoles.Init }]));
        await Assert.ThrowsAsync<ArgumentException>(() => router.RunPipelineAsync(
            [new GenerationStage(Image, Order("sd")) { SelectInput = _ => [] }]));

        Assert.Equal(0, router.Calls);   // eager: it threw before any stage ran
    }

    [Fact]
    public async Task A_spend_cap_binds_BETWEEN_stages_because_the_runner_drives_the_ROUTER_not_a_backend()
    {
        // The fact this whole design exists to keep. `pitfalls.md` § Second doors: a capability enforced at
        // one entry point is not enforced when a second reaches the same objects, and a pipeline is a new
        // door. This FAILS the day anyone moves the runner below IGenerationRouter — which no review
        // reliably catches, because the two paths share state rather than code.
        var backend = new FakeGenerationProvider { Id = "hosted", CostUsd = 0.40 };
        var options = new LyntaiOptions();
        options.Budget.MaxCostUsd = 0.30;
        var tracker = new InMemoryUsageTracker();
        var router = new BudgetedGenerationRouter(
            new GenerationRouter([backend], null,
                new DeadHostTracker(threshold: 5, cooldown: TimeSpan.FromMinutes(5))),
            tracker, options);

        var result = await router.RunPipelineAsync(
        [
            new GenerationStage(Image, Order("hosted")),
            new GenerationStage(Image, Order("hosted")) { InputRole = GenerationInputRoles.Init },
        ]);

        Assert.Equal(1, result.FailedAt);
        Assert.Equal(GenerationVerdict.Refused, result.Verdict);
        Assert.Contains("cost budget", result.Detail);
        Assert.Equal(1, backend.GenerateCalls);            // stage 1 ran and spent; stage 2 never reached it
        Assert.Equal(0.40, result.Usage!.CostUsd!.Value, 6);
    }

    /// <summary>A router whose answers are scripted, recording what it was asked. <see cref="Calls"/> is what
    /// proves a stage did — or did NOT — reach a backend.
    /// <para>The submit and stream doors THROW: the pipeline drives the inline door only, so reaching either
    /// is the defect, and a plausible return value would hide it.</para></summary>
    private sealed class ScriptedRouter(params GenerationResult[] script) : IGenerationRouter
    {
        private readonly Queue<GenerationResult> _script = new(script);

        public List<GenerationRequest> Requests { get; } = [];

        public List<IReadOnlyList<GenerationCandidate>> Candidates { get; } = [];

        public int Calls => Requests.Count;

        public Task<GenerationResult> GenerateAsync(
            IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Requests.Add(request);
            Candidates.Add(candidates);
            return Task.FromResult(_script.Count > 0
                ? _script.Dequeue()
                : GenerationResult.Failure(GenerationVerdict.Failed, "the script ran out"));
        }

        public Task<GenerationSubmission> SubmitAsync(
            IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException("the pipeline must drive the inline door");

        public IAsyncEnumerable<GenerationChunk> StreamAsync(
            IReadOnlyList<GenerationCandidate> candidates, GenerationRequest request,
            CancellationToken ct = default) =>
            throw new NotSupportedException("the pipeline must drive the inline door");
    }
}
