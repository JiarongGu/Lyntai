using Lyntai.Generation;
using Lyntai.Generation.Routing;
using Lyntai.Lifecycle;
using Lyntai.Llm;
using Lyntai.Llm.Routing;
using Lyntai.Tests.Fakes;

namespace Lyntai.Tests.Lifecycle;

/// <summary>Both routers key dead-host cooldown — and, for the generation side, concurrency admission — on the
/// CONFIGURATION a provider is running under rather than on its id, so that several configurations of one
/// backend can be live at once without one tenant's rate limit benching every other tenant.
///
/// <para>The default (no delegate) path is asserted first and deliberately: an app that never opts in must
/// behave exactly as it did before this seam existed.</para></summary>
public class RouterCooldownKeyTests
{
    private static GenerationRequest Request() => new() { Kind = GenerationKinds.Image, Prompt = "a cat" };

    private static List<GenerationCandidate> Candidates(params string[] ids) =>
        [.. ids.Select(id => new GenerationCandidate(id))];

    /// <summary>How long a bounded await waits before failing the test outright. Generous enough never to
    /// fire on a loaded machine, short enough that the failure is legible.
    ///
    /// <para>Every await on a gated call in this class is bounded by it, because the regression these tests
    /// exist to catch — a permit that is never returned — makes the waiting caller wait FOREVER. An
    /// unbounded await would turn that regression into an indefinite hang: <c>verify</c> stops producing
    /// output at all and no test names the problem, which destroys the signal for every other test in the
    /// run. A <see cref="TimeoutException"/> at the assertion that matters is the difference between a red
    /// test and a dead run.</para></summary>
    private static readonly TimeSpan GateWait = TimeSpan.FromSeconds(5);

    // Default behaviour must be untouched: an app that never supplies a delegate benches by provider id,
    // exactly as it does today.
    [Fact]
    public async Task Without_a_delegate_the_cooldown_key_is_still_the_provider_id()
    {
        var tracker = new DeadHostTracker(threshold: 1);
        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.Failed);
        var spare = new FakeGenerationProvider { Id = "comfyui" };
        var router = new GenerationRouter([failing, spare], null, tracker);

        await router.GenerateAsync(Candidates("a1111", "comfyui"), Request());

        Assert.True(tracker.IsDead("generation::a1111"));
    }

    // Two instances of the SAME backend id on different configurations must bench independently —
    // otherwise one tenant's exhausted quota takes out every other tenant.
    [Fact]
    public async Task A_configuration_delegate_separates_two_configurations_of_one_backend_id()
    {
        var tracker = new DeadHostTracker(threshold: 1);
        var cfgA = ProviderKey.For("openai-images").With("tenant", "a").Build();
        var cfgB = ProviderKey.For("openai-images").With("tenant", "b").Build();

        var tenantA = new FakeGenerationProvider { Id = "openai-images" };
        tenantA.Verdicts.Enqueue(GenerationVerdict.RateLimited);

        var router = new GenerationRouter([tenantA], null, tracker, _ => cfgA);
        await router.GenerateAsync(Candidates("openai-images"), Request());

        Assert.True(tracker.IsDead($"generation::{cfgA}"));
        Assert.False(tracker.IsDead($"generation::{cfgB}"));
        Assert.False(tracker.IsDead("generation::openai-images"));
    }

    // A null return is what a container-composed provider yields, and must mean "behave as before".
    [Fact]
    public async Task A_delegate_returning_null_falls_back_to_the_provider_id()
    {
        var tracker = new DeadHostTracker(threshold: 1);
        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.RateLimited);

        var router = new GenerationRouter([failing], null, tracker, _ => null);
        await router.GenerateAsync(Candidates("a1111"), Request());

        Assert.True(tracker.IsDead("generation::a1111"));
    }

    // The rev-2 regression: the limit must bound calls even though instances may be per-call.
    [Fact]
    public async Task Admission_bounds_concurrent_attempts_for_one_configuration()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["a1111"] = 1;
        var admission = new ProviderAdmission(options);
        var key = ProviderKey.For("a1111").With("v", "a").Build();

        var backend = new BlockingGenerationProvider { Id = "a1111" };
        var router = new GenerationRouter([backend], null, new DeadHostTracker(), _ => key, admission);

        var first = router.GenerateAsync(Candidates("a1111"), Request());
        await backend.Entered.Task.WaitAsync(GateWait);   // first attempt is inside the provider
        var second = router.GenerateAsync(Candidates("a1111"), Request());

        Assert.False(second.IsCompleted);                 // held at the gate, not at the backend
        Assert.Equal(1, backend.Concurrent);

        backend.Release();
        await first.WaitAsync(GateWait);
        // bounded deliberately: if the permit `first` took is ever leaked, this is the await that would
        // otherwise hang the whole run instead of failing (see GateWait)
        await second.WaitAsync(GateWait);
    }

    // Every permit taken must come back, or the gate is pinned for the life of the process. Asserting the
    // gate table is empty afterwards is the only check that catches a `return` that skipped the dispose.
    [Fact]
    public async Task Every_admitted_attempt_releases_its_permit()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["a1111"] = 1;
        var admission = new ProviderAdmission(options);
        var key = ProviderKey.For("a1111").With("v", "a").Build();

        var failing = new FakeGenerationProvider { Id = "a1111" };
        failing.Verdicts.Enqueue(GenerationVerdict.Refused);       // Surface: returns from mid-attempt
        var router = new GenerationRouter([failing], null, new DeadHostTracker(), _ => key, admission);

        await router.GenerateAsync(Candidates("a1111"), Request());

        Assert.Equal(0, admission.GateCount);
        // and the gate still admits, which a leaked permit on a limit of 1 would prevent
        var next = admission.EnterAsync(key, CancellationToken.None);
        Assert.True(next.IsCompleted);
        (await next).Dispose();
    }

    // A provider that THROWS is the other path a `using` has to cover — the router lets the throw out of
    // GenerateAsync, so the permit is only returned if it was scoped rather than released by hand.
    [Fact]
    public async Task A_throwing_backend_still_releases_its_permit()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["a1111"] = 1;
        var admission = new ProviderAdmission(options);
        var key = ProviderKey.For("a1111").With("v", "a").Build();

        var router = new GenerationRouter(
            [new ThrowingGenerationProvider { Id = "a1111" }], null, new DeadHostTracker(), _ => key, admission);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.GenerateAsync(Candidates("a1111"), Request()));

        Assert.Equal(0, admission.GateCount);
    }

    [Fact]
    public async Task The_llm_router_honours_the_delegate_too()
    {
        var tracker = new DeadHostTracker(threshold: 1);
        var provider = new FakeLlmProvider("openai");
        provider.Replies.Enqueue(new LlmReply("nope", LlmVerdict.RateLimited));
        var cfg = ProviderKey.For("openai").With("tenant", "a").Build();

        var router = new LlmRouter([provider], tracker, new LyntaiOptions(), configuration: _ => cfg);

        await router.CompleteAsync([new LlmCandidate("openai")],
            new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.True(tracker.IsDead(cfg.ToString()));
        Assert.False(tracker.IsDead("openai"));
    }

    // Identity by configuration and granularity by model are ORTHOGONAL, so they compose rather than one
    // replacing the other. The full table the two knobs produce:
    //   no delegate + Provider          -> "openai"
    //   no delegate + ProviderAndModel  -> "openai::gpt-5"        (covered by the existing router tests)
    //   delegate    + Provider          -> "openai#<fp12>"        (covered above)
    //   delegate    + ProviderAndModel  -> "openai#<fp12>::gpt-5" (this test — the only untested cell)
    [Fact]
    public async Task The_delegate_composes_with_the_ProviderAndModel_cooldown_scope()
    {
        var tracker = new DeadHostTracker(threshold: 1);
        var options = new LyntaiOptions();
        options.Routing.CooldownScope = CooldownScope.ProviderAndModel;
        var cfg = ProviderKey.For("openai").With("tenant", "a").Build();

        var provider = new FakeLlmProvider("openai");
        provider.Replies.Enqueue(new LlmReply("nope", LlmVerdict.RateLimited));

        var router = new LlmRouter([provider], tracker, options, configuration: _ => cfg);

        await router.CompleteAsync([new LlmCandidate("openai", "gpt-5")],
            new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.True(tracker.IsDead($"{cfg}::gpt-5"));      // the configuration AND the model
        Assert.False(tracker.IsDead(cfg.ToString()));      // not the configuration alone
        Assert.False(tracker.IsDead("openai::gpt-5"));     // and never the bare provider id
    }

    // The LLM side gates completions the same way, and must return the permit on the fallback path too —
    // a RateLimited reply returns from the middle of the attempt, which is where a hand-rolled release goes
    // missing.
    [Fact]
    public async Task The_llm_router_releases_its_permit_on_the_fallback_path()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["openai"] = 1;
        var admission = new ProviderAdmission(options);
        var cfg = ProviderKey.For("openai").With("tenant", "a").Build();

        var provider = new FakeLlmProvider("openai");
        provider.Replies.Enqueue(new LlmReply("nope", LlmVerdict.RateLimited));

        var router = new LlmRouter([provider], new DeadHostTracker(), new LyntaiOptions(),
            configuration: _ => cfg, admission: admission);

        await router.CompleteAsync([new LlmCandidate("openai")],
            new LlmRequest { Messages = [LlmMessage.User("hi")] });

        Assert.Equal(0, admission.GateCount);
    }

    // The documented asymmetry: streaming is NOT gated, because a stream holds a permit for the whole
    // response and the router cannot fall back after the first token. Pinning it here makes a later change
    // to that decision deliberate rather than accidental.
    [Fact]
    public async Task Streaming_is_deliberately_not_gated()
    {
        var options = new ProviderAdmissionOptions();
        options.BySlot["openai"] = 1;
        var admission = new ProviderAdmission(options);
        var cfg = ProviderKey.For("openai").With("tenant", "a").Build();

        var router = new LlmRouter([new FakeLlmProvider("openai")], new DeadHostTracker(), new LyntaiOptions(),
            configuration: _ => cfg, admission: admission);

        var chunks = new List<LlmChunk>();
        await foreach (var chunk in router.StreamAsync([new LlmCandidate("openai")],
                           new LlmRequest { Messages = [LlmMessage.User("hi")] }))
        {
            // mid-stream the gate must be untouched — the stream never entered it
            Assert.Equal(0, admission.GateCount);
            chunks.Add(chunk);
        }

        Assert.NotEmpty(chunks);
        Assert.Equal(0, admission.GateCount);
    }

    /// <summary>Blocks inside GenerateAsync so a second caller can be observed queueing at the gate.</summary>
    private sealed class BlockingGenerationProvider : IGenerationProvider
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrent;

        public string Id { get; init; } = "a1111";
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Concurrent => Volatile.Read(ref _concurrent);

        public GenerationCapabilities Capabilities { get; } = new()
        {
            Kinds = [GenerationKinds.Image],
            Deliveries = [GenerationDelivery.Inline],
        };

        public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new GenerationProbeResult(true, "ready"));

        public async Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _concurrent);
            Entered.TrySetResult();
            await _gate.Task.ConfigureAwait(false);
            Interlocked.Decrement(ref _concurrent);
            return GenerationResult.Success([new GenerationArtifact("image/png", Data: [0x89])]);
        }

        public void Release() => _gate.TrySetResult();
    }

    /// <summary>Violates the fail-safe contract on purpose: the permit must come back anyway.</summary>
    private sealed class ThrowingGenerationProvider : IGenerationProvider
    {
        public string Id { get; init; } = "a1111";

        public GenerationCapabilities Capabilities { get; } = new()
        {
            Kinds = [GenerationKinds.Image],
            Deliveries = [GenerationDelivery.Inline],
        };

        public Task<GenerationProbeResult> ProbeAsync(CancellationToken ct = default) =>
            Task.FromResult(new GenerationProbeResult(true, "ready"));

        public Task<GenerationResult> GenerateAsync(GenerationRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("backend blew up");
    }
}
