using System.Diagnostics;
using System.Diagnostics.Metrics;
using Lyntai.Diagnostics;
using Lyntai.Generation;
using Lyntai.Generation.Jobs;
using Lyntai.Generation.Routing;
using Lyntai.Llm.Budgeting;
using Lyntai.Llm.RateLimiting;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Lyntai.Tests.Generation;

/// <summary>Governance for the generation domain: dead-host cooldown, spend caps, throttling and telemetry.
/// Every piece REUSES the LLM side's machinery (<see cref="DeadHostTracker"/>, <see cref="IUsageTracker"/>,
/// <see cref="IRateLimiter"/>) rather than growing a second copy — so these tests are mostly about the
/// WIRING being right, plus the two places the domains must NOT bleed into each other (cooldown keys and
/// rate-limit buckets).</summary>
public class GenerationGovernanceTests
{
    private static readonly GenerationRequest Image = new() { Kind = GenerationKinds.Image, Prompt = "a red square" };
    private static readonly GenerationRequest Video = new() { Kind = GenerationKinds.Video, Prompt = "a cat surfing" };

    private static IReadOnlyList<GenerationCandidate> Order(params string[] ids) =>
        [.. ids.Select(id => new GenerationCandidate(id))];

    // ---- dead-host cooldown --------------------------------------------------------------------------

    [Fact]
    public async Task A_backend_that_rate_limits_is_benched_so_the_next_render_does_not_ask_it_again()
    {
        // a 429 is the backend telling us to stop; re-asking inside the window is always wasted
        var limited = new FakeGenerationProvider { Id = "hosted" };
        limited.Verdicts.Enqueue(GenerationVerdict.RateLimited);
        var healthy = new FakeGenerationProvider { Id = "local" };
        var router = Router([limited, healthy]);

        var first = await router.GenerateAsync(Order("hosted", "local"), Image);
        var second = await router.GenerateAsync(Order("hosted", "local"), Image);

        Assert.True(first.IsOk);
        Assert.True(second.IsOk);
        Assert.Equal(1, limited.GenerateCalls);      // asked once, benched, skipped on the second run
        Assert.Equal(2, healthy.GenerateCalls);
    }

    [Fact]
    public async Task A_transient_failure_takes_the_whole_threshold_before_a_backend_is_benched()
    {
        // one dropped connection is not a dead host — the threshold is what distinguishes them
        var flaky = new FakeGenerationProvider { Id = "flaky" };
        flaky.Verdicts.Enqueue(GenerationVerdict.Failed);
        var healthy = new FakeGenerationProvider { Id = "local" };
        var router = Router([flaky, healthy], new DeadHostTracker(threshold: 2, cooldown: TimeSpan.FromMinutes(5)));

        for (var i = 0; i < 3; i++) await router.GenerateAsync(Order("flaky", "local"), Image);

        Assert.Equal(2, flaky.GenerateCalls);        // failed twice, then benched for the third run
    }

    [Fact]
    public async Task A_success_clears_the_penalty_so_an_occasional_blip_never_accumulates()
    {
        var blippy = new FakeGenerationProvider { Id = "blippy" };
        blippy.Verdicts.Enqueue(GenerationVerdict.Failed);
        blippy.Verdicts.Enqueue(GenerationVerdict.Ok);
        var router = Router([blippy, new FakeGenerationProvider { Id = "local" }],
            new DeadHostTracker(threshold: 2, cooldown: TimeSpan.FromMinutes(5)));

        await router.GenerateAsync(Order("blippy", "local"), Image);   // fail  → 1 strike
        await router.GenerateAsync(Order("blippy", "local"), Image);   // ok    → cleared
        await router.GenerateAsync(Order("blippy", "local"), Image);   // ok

        Assert.Equal(3, blippy.GenerateCalls);
    }

    [Fact]
    public async Task A_capability_gap_never_counts_against_a_backend()
    {
        // "not configured" / "not for me" are not faults: benching for them would take a backend out of
        // rotation for being honest about what it is
        var unconfigured = new FakeGenerationProvider { Id = "needs-setup" };
        unconfigured.Verdicts.Enqueue(GenerationVerdict.NotConfigured);
        var router = Router([unconfigured, new FakeGenerationProvider { Id = "local" }],
            new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5)));

        await router.GenerateAsync(Order("needs-setup", "local"), Image);
        await router.GenerateAsync(Order("needs-setup", "local"), Image);

        Assert.Equal(2, unconfigured.GenerateCalls);
    }

    [Fact]
    public async Task The_only_capable_backend_is_never_benched()
    {
        // benching the sole option just converts a real error into a synthetic one, and the host can't act
        // on "no capable backend" the way it can act on "rate limited"
        var sole = new FakeGenerationProvider { Id = "sole" };
        sole.Verdicts.Enqueue(GenerationVerdict.RateLimited);
        var router = Router([sole]);

        var first = await router.GenerateAsync(Order("sole"), Image);
        var second = await router.GenerateAsync(Order("sole"), Image);

        Assert.Equal(GenerationVerdict.RateLimited, first.Verdict);
        Assert.Equal(GenerationVerdict.RateLimited, second.Verdict);   // a real verdict, not a fabricated one
        Assert.Equal(2, sole.GenerateCalls);
    }

    [Fact]
    public async Task Cooldown_keys_are_domain_scoped_so_a_chat_outage_never_benches_a_generation_backend()
    {
        // the tracker is SHARED with the LLM router (one copy of the machinery, one config), which means a
        // host whose chat provider and image backend share an id would otherwise cross-penalise them
        var deadHosts = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        deadHosts.MarkDead("openai");                       // the LLM router's key for a chat provider
        var backend = new FakeGenerationProvider { Id = "openai" };
        var router = Router([backend, new FakeGenerationProvider { Id = "local" }], deadHosts);

        var result = await router.GenerateAsync(Order("openai", "local"), Image);

        Assert.True(result.IsOk);
        Assert.Equal(1, backend.GenerateCalls);
    }

    [Fact]
    public async Task When_every_capable_backend_is_benched_the_reason_says_so()
    {
        var a = new FakeGenerationProvider { Id = "a" };
        a.Verdicts.Enqueue(GenerationVerdict.RateLimited);
        var b = new FakeGenerationProvider { Id = "b" };
        b.Verdicts.Enqueue(GenerationVerdict.RateLimited);
        var router = Router([a, b]);

        await router.GenerateAsync(Order("a", "b"), Image);
        var result = await router.GenerateAsync(Order("a", "b"), Image);

        Assert.False(result.IsOk);
        Assert.Contains("cooldown", result.Detail);
    }

    [Fact]
    public async Task A_benched_backend_returns_to_rotation_when_its_window_expires()
    {
        var now = DateTimeOffset.UnixEpoch;
        var limited = new FakeGenerationProvider { Id = "hosted" };
        limited.Verdicts.Enqueue(GenerationVerdict.RateLimited);
        var router = Router([limited, new FakeGenerationProvider { Id = "local" }],
            new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromSeconds(30), clock: () => now));

        await router.GenerateAsync(Order("hosted", "local"), Image);
        now += TimeSpan.FromSeconds(31);
        await router.GenerateAsync(Order("hosted", "local"), Image);

        Assert.Equal(2, limited.GenerateCalls);
    }

    [Fact]
    public async Task A_submission_also_benches_a_backend_that_refuses_to_take_the_job()
    {
        // a paid render's submit path needs the same protection as the inline one
        var deadHosts = new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5));
        var broken = new BrokenSubmitProvider { Id = "broken" };
        var working = new FakeGenerationJobProvider { Id = "working" };
        var router = Router([broken, working], deadHosts);

        var first = await router.SubmitAsync(Order("broken", "working"), Video);
        var second = await router.SubmitAsync(Order("broken", "working"), Video);

        Assert.Equal("working", first.ProviderId);
        Assert.Equal("working", second.ProviderId);
        Assert.Equal(1, broken.SubmitCalls);
    }

    // ---- spend caps ----------------------------------------------------------------------------------

    [Fact]
    public async Task Once_the_cost_cap_is_reached_a_render_is_refused_without_calling_a_backend()
    {
        var backend = new FakeGenerationProvider { Id = "hosted", CostUsd = 0.40 };
        var (router, _) = Budgeted(backend, options => options.Budget.MaxCostUsd = 0.50);

        var first = await router.GenerateAsync(Order("hosted"), Image);    // spends 0.40
        var second = await router.GenerateAsync(Order("hosted"), Image);   // spends 0.80 → over
        var third = await router.GenerateAsync(Order("hosted"), Image);

        Assert.True(first.IsOk);
        Assert.True(second.IsOk);                                          // soft ceiling: the crossing call runs
        Assert.Equal(GenerationVerdict.Refused, third.Verdict);
        Assert.Contains("cost budget", third.Detail);
        Assert.Equal(2, backend.GenerateCalls);                            // the refusal never reached it
    }

    [Fact]
    public async Task Render_spend_lands_in_the_SAME_wallet_as_chat_spend()
    {
        // "what did my app spend today" has to be ONE number — a separate media tracker would answer a
        // question nobody asks
        var backend = new FakeGenerationProvider { Id = "hosted", CostUsd = 0.25 };
        var (router, tracker) = Budgeted(backend, _ => { });
        await tracker.RecordAsync("default", new Lyntai.Llm.LlmUsage(100, 50, 0, 0.01));

        await router.GenerateAsync(Order("hosted"), Image);

        var total = await tracker.TotalAsync();
        Assert.Equal(0.26, total.CostUsd, 6);
        Assert.Equal(150, total.TotalTokens);      // the render contributed no tokens, and claimed none
    }

    [Fact]
    public async Task A_token_cap_alone_never_blocks_a_render()
    {
        // a render spends no tokens, so refusing one because chat exhausted its token budget would be
        // governance by coincidence
        var backend = new FakeGenerationProvider { Id = "hosted" };
        var (router, tracker) = Budgeted(backend, options => options.Budget.MaxTokens = 10);
        await tracker.RecordAsync("default", new Lyntai.Llm.LlmUsage(1_000, 1_000));

        var result = await router.GenerateAsync(Order("hosted"), Image);

        Assert.True(result.IsOk);
    }

    [Fact]
    public async Task A_per_consumer_cap_binds_only_that_consumer()
    {
        var backend = new FakeGenerationProvider { Id = "hosted", CostUsd = 1.0 };
        var (router, _) = Budgeted(backend, options =>
            options.Budget.PerConsumer["agent"] = new ConsumerBudget(MaxCostUsd: 0.50));

        var agent = await router.GenerateAsync(Order("hosted"), Image with { Consumer = "agent" });
        var agentAgain = await router.GenerateAsync(Order("hosted"), Image with { Consumer = "agent" });
        var ui = await router.GenerateAsync(Order("hosted"), Image with { Consumer = "ui" });

        Assert.True(agent.IsOk);
        Assert.Equal(GenerationVerdict.Refused, agentAgain.Verdict);
        Assert.Contains("agent", agentAgain.Detail);
        Assert.True(ui.IsOk);
    }

    [Fact]
    public async Task A_submission_is_refused_over_budget_because_submitting_is_what_commits_the_money()
    {
        var backend = new FakeGenerationJobProvider { Id = "video" };
        var (router, tracker) = Budgeted(backend, options => options.Budget.MaxCostUsd = 1.0);
        await tracker.RecordAsync("default", new Lyntai.Llm.LlmUsage(0, 0, 0, 2.0));

        var submission = await router.SubmitAsync(Order("video"), Video);

        Assert.Equal(GenerationOperationStatus.Failed, submission.Operation.Status);
        Assert.Contains("cost budget", submission.Operation.Detail);
        Assert.Equal(0, backend.SubmitCalls);
    }

    // ---- throttling ----------------------------------------------------------------------------------

    [Fact]
    public async Task Over_the_configured_rate_a_render_is_refused_rather_than_queued()
    {
        var backend = new FakeGenerationProvider { Id = "hosted" };
        var limits = new RateLimitOptions { PermitsPerSecond = 1, Burst = 1, MaxWait = TimeSpan.Zero };
        var router = new RateLimitedGenerationRouter(Router([backend]), new TokenBucketRateLimiter(limits));

        var first = await router.GenerateAsync(Order("hosted"), Image);
        var second = await router.GenerateAsync(Order("hosted"), Image);

        Assert.True(first.IsOk);
        Assert.Equal(GenerationVerdict.RateLimited, second.Verdict);
        Assert.Equal(1, backend.GenerateCalls);
    }

    [Fact]
    public void Generation_throttling_is_configured_separately_from_chat_throttling()
    {
        // a render and a chat turn hit different vendors' rate limits; one shared bucket would have an image
        // render starve the chat that requested it
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => new FakeGenerationProvider { Id = "hosted" })
            .AddGenerationRateLimit(limits => limits.PermitsPerSecond = 3));
        using var sp = services.BuildServiceProvider();

        Assert.Equal(3, sp.GetRequiredService<GenerationOptions>().RateLimit.PermitsPerSecond);
        Assert.Equal(0, sp.GetRequiredService<LyntaiOptions>().RateLimit.PermitsPerSecond);   // chat untouched
    }

    [Fact]
    public async Task The_configured_governance_wraps_the_router_the_app_resolves()
    {
        // the decorators are useless if AddLyntai hands the app the bare router
        var backend = new FakeGenerationProvider { Id = "hosted", CostUsd = 5.0 };
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddLyntai(cfg => cfg
            .AddGenerationProvider(_ => backend)
            .UseDefaultGenerationCandidates("hosted")
            .AddGenerationUsageBudget(budget => budget.MaxCostUsd = 1.0)
            .AddGenerationRateLimit(limits => limits.PermitsPerSecond = 100));
        using var sp = services.BuildServiceProvider();
        var router = sp.GetRequiredService<IGenerationRouter>();

        var first = await router.GenerateAsync(Order("hosted"), Image);
        var second = await router.GenerateAsync(Order("hosted"), Image);

        Assert.True(first.IsOk);
        Assert.Equal(GenerationVerdict.Refused, second.Verdict);          // the budget decorator is in the chain
        Assert.Equal(5.0, (await sp.GetRequiredService<IUsageTracker>().TotalAsync()).CostUsd, 6);
    }

    // ---- telemetry -----------------------------------------------------------------------------------

    [Fact]
    public async Task An_inline_render_emits_a_span_naming_the_backend_the_medium_and_the_model()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);
        var router = Router([new FakeGenerationProvider { Id = "tel-ok" }]);

        await router.GenerateAsync([new GenerationCandidate("tel-ok", "sdxl")], Image);

        var span = Assert.Single(SpansWith(spans, "gen_ai.system", "tel-ok"));
        Assert.Equal("generate image", span.DisplayName);
        Assert.Equal("sdxl", span.GetTagItem("gen_ai.request.model"));
        Assert.Equal(1, span.GetTagItem("lyntai.generation.artifacts"));
        Assert.Equal(ActivityStatusCode.Unset, span.Status);
    }

    [Fact]
    public async Task A_failed_attempt_marks_its_span_with_the_verdict()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);
        var failing = new FakeGenerationProvider { Id = "tel-fail" };
        failing.Verdicts.Enqueue(GenerationVerdict.Timeout);
        var router = Router([failing, new FakeGenerationProvider { Id = "tel-backup" }]);

        await router.GenerateAsync(Order("tel-fail", "tel-backup"), Image);

        var span = Assert.Single(SpansWith(spans, "gen_ai.system", "tel-fail"));
        Assert.Equal("Timeout", span.GetTagItem("error.type"));
        Assert.Equal(ActivityStatusCode.Error, span.Status);
        // the fallback attempt is its own span, so a trace shows both
        Assert.Single(SpansWith(spans, "gen_ai.system", "tel-backup"));
    }

    [Fact]
    public async Task A_submission_gets_its_own_span_so_a_durable_render_is_traceable_from_the_start()
    {
        var spans = new List<Activity>();
        using var listener = SpanListener(spans);
        ActivitySource.AddActivityListener(listener);
        var router = Router([new FakeGenerationJobProvider { Id = "tel-video" }]);

        await router.SubmitAsync(Order("tel-video"), Video);

        var span = Assert.Single(SpansWith(spans, "gen_ai.system", "tel-video"));
        Assert.Equal("submit video", span.DisplayName);
        Assert.Equal("op-1", span.GetTagItem("lyntai.generation.operation_id"));
    }

    [Fact]
    public async Task What_a_render_cost_is_recorded_as_a_metric_because_that_is_the_number_hosts_watch()
    {
        var costs = new List<(double Value, string? Backend, string? Kind)>();
        using var meter = GenerationMeterListener("lyntai.generation.cost", (value, tags) =>
        {
            string? backend = null, kind = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "gen_ai.system") backend = (string?)tag.Value;
                if (tag.Key == "lyntai.generation.kind") kind = (string?)tag.Value;
            }
            lock (costs) costs.Add((value, backend, kind));
        });
        var router = Router([new FakeGenerationProvider { Id = "metric-cost", CostUsd = 0.12 }]);

        await router.GenerateAsync(Order("metric-cost"), Image);

        lock (costs) Assert.Contains((0.12, "metric-cost", GenerationKinds.Image), costs);
    }

    [Fact]
    public async Task Duration_is_recorded_per_attempt_tagged_with_the_failure_that_ended_it()
    {
        var samples = new List<(string? Backend, string? Error)>();
        using var meter = GenerationMeterListener("lyntai.generation.duration", (_, tags) =>
        {
            string? backend = null, error = null;
            foreach (var tag in tags)
            {
                if (tag.Key == "gen_ai.system") backend = (string?)tag.Value;
                if (tag.Key == "error.type") error = (string?)tag.Value;
            }
            lock (samples) samples.Add((backend, error));
        });
        var failing = new FakeGenerationProvider { Id = "metric-fail" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        var router = Router([failing, new FakeGenerationProvider { Id = "metric-ok" }]);

        await router.GenerateAsync(Order("metric-fail", "metric-ok"), Image);

        lock (samples)
        {
            Assert.Contains(("metric-fail", "Failed"), samples);
            Assert.Contains(("metric-ok", null), samples);
        }
    }

    // ---- consumer attribution ------------------------------------------------------------------------

    [Fact]
    public void The_consumer_tag_survives_a_durable_job_payload()
    {
        // a render that resumes in another process must still bill to whoever asked for it
        var job = new GenerationRenderJob(["video"], Video with { Consumer = "agent" });

        var parsed = GenerationRenderJob.Parse(job.ToJson());

        Assert.Equal("agent", parsed?.Request.Consumer);
    }

    [Fact]
    public void An_agent_driven_render_is_attributed_to_the_agent_by_default()
    {
        // the runaway-spend risk is a tool loop, not a human clicking a button — so agent renders are
        // capped separately out of the box
        var tool = new Lyntai.Generation.Tools.GenerationInlineTool(
            Router([new FakeGenerationProvider { Id = "hosted" }]), new GenerationOptions());

        Assert.Equal("agent", tool.Consumer);
    }

    // ---- helpers -------------------------------------------------------------------------------------

    /// <summary>A router over <paramref name="backends"/> with cooldown enabled (threshold 1 unless a tracker
    /// is supplied — one failure benches, which is what makes the cooldown tests short).</summary>
    private static GenerationRouter Router(IGenerationProvider[] backends, DeadHostTracker? deadHosts = null) =>
        new(backends, null, deadHosts ?? new DeadHostTracker(threshold: 1, cooldown: TimeSpan.FromMinutes(5)));

    private static (IGenerationRouter Router, IUsageTracker Tracker) Budgeted(
        IGenerationProvider backend, Action<LyntaiOptions> configure)
    {
        var options = new LyntaiOptions();
        configure(options);
        var tracker = new InMemoryUsageTracker();
        return (new BudgetedGenerationRouter(Router([backend]), tracker, options), tracker);
    }

    private static ActivityListener SpanListener(List<Activity> sink) => new()
    {
        ShouldListenTo = s => s.Name == LyntaiDiagnostics.GenerationActivitySourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
        ActivityStopped = a => { lock (sink) sink.Add(a); },
    };

    private static List<Activity> SpansWith(List<Activity> spans, string tag, object value)
    {
        lock (spans) return [.. spans.Where(s => Equals(s.GetTagItem(tag), value))];
    }

    private static MeterListener GenerationMeterListener(string instrument,
        Action<double, ReadOnlySpan<KeyValuePair<string, object?>>> onMeasure)
    {
        var listener = new MeterListener
        {
            InstrumentPublished = (inst, l) =>
            {
                if (inst.Meter.Name == LyntaiDiagnostics.GenerationMeterName && inst.Name == instrument)
                    l.EnableMeasurementEvents(inst);
            },
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) => onMeasure(value, tags));
        listener.SetMeasurementEventCallback<double>((_, value, tags, _) => onMeasure(value, tags));
        listener.Start();
        return listener;
    }

    /// <summary>A job backend whose submissions always fail — the submit-path counterpart of a dead host.</summary>
    private sealed class BrokenSubmitProvider : IGenerationProvider, IGenerationJobProvider
    {
        public string Id { get; init; } = "broken";
        public int SubmitCalls { get; private set; }

        public GenerationCapabilities Capabilities { get; } = new()
        {
            Kinds = [GenerationKinds.Video],
            Deliveries = [GenerationDelivery.Job],
        };

        public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new GenerationProbeResult(true, "up"));

        public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
            Task.FromResult(GenerationResult.Failure(GenerationVerdict.Unsupported, "job backend"));

        public Task<GenerationOperation> SubmitAsync(GenerationRequest request, CancellationToken ct = default)
        {
            SubmitCalls++;
            return Task.FromResult(new GenerationOperation("", GenerationOperationStatus.Failed, Detail: "queue down"));
        }

        public Task<GenerationOperation> PollAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Failed));

        public Task<GenerationResult> FetchAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(GenerationResult.Failure(GenerationVerdict.Failed, "nothing"));

        public Task<GenerationOperation> CancelAsync(string operationId, CancellationToken ct = default) =>
            Task.FromResult(new GenerationOperation(operationId, GenerationOperationStatus.Cancelled));
    }
}
