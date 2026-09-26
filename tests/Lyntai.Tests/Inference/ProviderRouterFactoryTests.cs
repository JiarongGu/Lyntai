using Lyntai.Inference;

namespace Lyntai.Tests.Inference;

/// <summary>What the factory is FOR: the bookkeeping survives between calls.
///
/// <para>A router is cheap to rebuild and is rebuilt per call on these paths. The tracker is not — a
/// consumer that rebuilds it along with the router can never bench a failing backend, because the knowledge
/// that it is failing is thrown away in between: for the vector and score kinds (<b>D153</b>), a backend
/// answering 429 would be asked again on the very next recall.</para>
///
/// <para>The pair of tests below is deliberately a BEFORE and an AFTER over the same scenario, because the
/// only thing that distinguishes them is where the tracker lives.</para></summary>
public class ProviderRouterFactoryTests
{
    private sealed class StubVectorProvider(string id) : IVectorProvider
    {
        public string Id { get; } = id;
        public bool IsAvailable => true;
        public int Calls { get; private set; }
        public bool Fails { get; init; }

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Vector],
            Operations = [ProviderOperation.Complete],
        };

        public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Fails
                ? Task.FromResult(VectorResponse.Failure(ProviderVerdict.Failed, "down"))
                : Task.FromResult(VectorResponse.Success([[1f, 2f]]));
        }
    }

    // TWO capable backends on purpose: RoutingPolicy.ExemptSoleCandidate defaults to true, so a lone
    // candidate is asked even while benched — a one-backend scenario would prove nothing either way.
    private static (StubVectorProvider Bad, StubVectorProvider Good, IModelProvider[] All) Backends()
    {
        var bad = new StubVectorProvider("bad") { Fails = true };
        var good = new StubVectorProvider("good");
        return (bad, good, [bad, good]);
    }

    private static Task<VectorResponse> CallAsync(ProviderRouter<VectorRequest, VectorResponse> router) =>
        router.CallAsync(new VectorRequest(["hello"]));

    private static ProviderRouter<VectorRequest, VectorResponse> Bare(IEnumerable<IModelProvider> providers) =>
        new(providers, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));

    [Fact]
    public async Task A_router_rebuilt_per_call_with_its_own_tracker_asks_the_failing_backend_every_time()
    {
        // Pinned so it cannot come back as an "optimisation": rebuilding the bookkeeping with the router is
        // indistinguishable from having none.
        var (bad, good, all) = Backends();

        for (var i = 0; i < 3; i++) Assert.True((await CallAsync(Bare(all))).IsOk);

        Assert.Equal(3, bad.Calls);
        Assert.Equal(3, good.Calls);
    }

    [Fact]
    public async Task The_factory_shares_ONE_tracker_so_a_failing_backend_is_benched_for_the_next_call()
    {
        var (bad, good, all) = Backends();
        var factory = new ProviderRouterFactory(new DeadHostTracker(threshold: 1));

        // a router per call, exactly as the call sites build one — only the tracker is shared
        for (var i = 0; i < 3; i++)
        {
            var router = factory.For<VectorRequest, VectorResponse>(
                all, VectorResponse.Failure,
                c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));
            Assert.True((await CallAsync(router)).IsOk);
        }

        Assert.Equal(1, bad.Calls);   // asked once, benched thereafter
        Assert.Equal(3, good.Calls);  // and the healthy one still answers every call
    }

    private sealed class FlakyVectorProvider(string id) : IVectorProvider
    {
        public string Id { get; } = id;
        public bool IsAvailable => true;
        public int Calls { get; private set; }

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Vector],
            Operations = [ProviderOperation.Complete],
        };

        public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default) =>
            Task.FromResult(++Calls == 1
                ? VectorResponse.Failure(ProviderVerdict.Failed, "transient")
                : VectorResponse.Success([[1f, 2f]]));
    }

    private sealed class StubScoreProvider(string id) : IScoreProvider
    {
        public string Id { get; } = id;
        public bool IsAvailable => true;

        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Score],
            Operations = [ProviderOperation.Complete],
        };

        public Task<ScoreResponse> CallAsync(ScoreRequest request, CancellationToken ct = default) =>
            Task.FromResult(ScoreResponse.Failure(ProviderVerdict.Failed, "down"));
    }

    [Fact]
    public async Task The_CONFIGURED_routing_policy_reaches_a_factory_built_router()
    {
        // ConfigureRouting must reach vector and score, not chat alone: a factory that did not pass the
        // configured policy would route those kinds on RoutingPolicy's defaults, silently ignoring an
        // operator's retries.
        var flaky = new FlakyVectorProvider("flaky");
        var options = new LyntaiOptions();
        options.Routing.Retry(ProviderVerdict.Failed, 1);
        var factory = new ProviderRouterFactory(new DeadHostTracker(), options: options);

        var router = factory.For<VectorRequest, VectorResponse>(
            new IModelProvider[] { flaky }, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));

        Assert.True((await CallAsync(router)).IsOk); // failed once, retried per the CONFIGURED policy
        Assert.Equal(2, flaky.Calls);
    }

    [Fact]
    public async Task An_EXPLICIT_policy_still_wins_over_the_configured_one()
    {
        var flaky = new FlakyVectorProvider("flaky");
        var options = new LyntaiOptions();
        options.Routing.Retry(ProviderVerdict.Failed, 1);
        var factory = new ProviderRouterFactory(new DeadHostTracker(), options: options);

        var router = factory.For<VectorRequest, VectorResponse>(
            new IModelProvider[] { flaky }, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text),
            policy: new RoutingPolicy()); // no retries

        Assert.False((await CallAsync(router)).IsOk);
        Assert.Equal(1, flaky.Calls);
    }

    [Fact]
    public async Task A_score_failure_never_benches_a_vector_backend_sharing_the_same_id()
    {
        // Reachable in a default configuration: AddOnnxProvider defaults Id = "onnx", so an embedder and a
        // reranker keyed on the bare id would share one bench — a failing reranker silencing recalls. The
        // factory scopes cooldown keys per closed shape ("vector::onnx" / "score::onnx"), the same rule
        // MediaRouter applies with its "generation::" prefix.
        var tracker = new DeadHostTracker(threshold: 1);
        var factory = new ProviderRouterFactory(tracker);
        var vector = new StubVectorProvider("onnx");
        var score = new StubScoreProvider("onnx");
        IModelProvider[] all = [vector, score];

        var scoreRouter = factory.For<ScoreRequest, ScoreResponse>(all, ScoreResponse.Failure,
            c => c.Supports(ProviderKinds.Score, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        Assert.False((await scoreRouter.CallAsync(new ScoreRequest("q", ["d"]))).IsOk); // benches score::onnx

        var vectorRouter = factory.For<VectorRequest, VectorResponse>(all, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        var reply = await CallAsync(vectorRouter);

        Assert.True(reply.IsOk);          // the embedder was ASKED — the reranker's bench is not its bench
        Assert.Equal(1, vector.Calls);
        // a lone candidate is asked even while benched, so the reply alone cannot tell; the keys can
        Assert.True(tracker.IsDead("score::onnx"));
        Assert.False(tracker.IsDead("vector::onnx"));
    }

    [Fact]
    public async Task A_null_provider_set_routes_to_nothing_rather_than_throwing()
    {
        // A consumer with nothing registered is an ordinary state on these paths — the memory seams pass
        // whatever the container holds, which may be empty.
        var factory = new ProviderRouterFactory(new DeadHostTracker());
        var router = factory.For<VectorRequest, VectorResponse>(null, VectorResponse.Failure);

        Assert.False(router.CanServe());
        Assert.False((await CallAsync(router)).IsOk);
    }

    // ---- governance: the one wallet reaches these kinds (D163) ----------------------------------------

    private sealed class StubLimiter(bool clears) : Lyntai.Inference.RateLimiting.IRateLimiter
    {
        public int Asked { get; private set; }
        public string? LastConsumer { get; private set; }

        public Task<bool> AcquireAsync(string consumer, CancellationToken ct = default)
        {
            Asked++;
            LastConsumer = consumer;
            return Task.FromResult(clears);
        }
    }

    private sealed record AppRequest(string Payload); // deliberately NOT IConsumerTagged
    private sealed record AppResponse(ProviderVerdict Verdict, string? Detail = null) : IProviderOutcome;

    private sealed class AppProvider : IProviderCall<AppRequest, AppResponse>
    {
        public string Id => "app";
        public bool IsAvailable => true;
        public int Calls { get; private set; }
        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = ["app-kind"],
            Operations = [ProviderOperation.Complete],
        };

        public Task<AppResponse> CallAsync(AppRequest request, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(new AppResponse(ProviderVerdict.Ok));
        }
    }

    private static LyntaiOptions CappedOptions(double? costCap = null, long? tokenCap = null)
    {
        var options = new LyntaiOptions();
        options.Budget.MaxCostUsd = costCap;
        options.Budget.MaxTokens = tokenCap;
        return options;
    }

    [Fact]
    public async Task A_reached_COST_cap_refuses_a_vector_call_without_asking_a_backend()
    {
        var (_, good, all) = Backends();
        var tracker = new Lyntai.Inference.Budgeting.InMemoryUsageTracker();
        await tracker.RecordAsync("app", new ProviderUsage(CostUsd: 2.0));
        var factory = new ProviderRouterFactory(new DeadHostTracker(),
            options: CappedOptions(costCap: 1.0), tracker: tracker);

        var router = factory.For<VectorRequest, VectorResponse>(all, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        var reply = await router.CallAsync(new VectorRequest(["a"], Consumer: "memory"));

        Assert.Equal(ProviderVerdict.Refused, reply.Verdict);
        Assert.Equal(0, good.Calls); // refused BEFORE any backend spent anything
    }

    [Fact]
    public async Task A_reached_TOKEN_cap_binds_these_kinds_because_they_are_token_metered()
    {
        var (_, good, all) = Backends();
        var tracker = new Lyntai.Inference.Budgeting.InMemoryUsageTracker();
        await tracker.RecordAsync("chat", new ProviderUsage(900, 200)); // 1100 > 1000
        var factory = new ProviderRouterFactory(new DeadHostTracker(),
            options: CappedOptions(tokenCap: 1_000), tracker: tracker);

        var router = factory.For<VectorRequest, VectorResponse>(all, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        var reply = await router.CallAsync(new VectorRequest(["a"], Consumer: "memory"));

        Assert.Equal(ProviderVerdict.Refused, reply.Verdict);
        Assert.Equal(0, good.Calls);
    }

    [Fact]
    public async Task Usage_the_wire_reported_is_recorded_under_the_request_consumer()
    {
        var tracker = new Lyntai.Inference.Budgeting.InMemoryUsageTracker();
        var reporting = new UsageReportingVectorProvider("v");
        var factory = new ProviderRouterFactory(new DeadHostTracker(),
            options: new LyntaiOptions(), tracker: tracker);

        var router = factory.For<VectorRequest, VectorResponse>([reporting], VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        var reply = await router.CallAsync(new VectorRequest(["a"], Consumer: "memory"));

        Assert.True(reply.IsOk);
        var totals = await tracker.TotalAsync("memory");
        Assert.Equal(7, totals.InputTokens);   // recorded, and under the STAMPED consumer
        Assert.Equal(1, totals.Calls);
    }

    private sealed class UsageReportingVectorProvider(string id) : IVectorProvider
    {
        public string Id { get; } = id;
        public bool IsAvailable => true;
        public ProviderCapabilities Capabilities { get; } = new()
        {
            Accepts = [ProviderKinds.Text],
            Produces = [ProviderKinds.Vector],
            Operations = [ProviderOperation.Complete],
        };

        public Task<VectorResponse> CallAsync(VectorRequest request, CancellationToken ct = default) =>
            Task.FromResult(VectorResponse.Success([[1f]], usage: new ProviderUsage(7)));
    }

    [Fact]
    public async Task A_rate_limiter_that_cannot_clear_refuses_as_RateLimited_without_benching_anyone()
    {
        var (_, good, all) = Backends();
        var limiter = new StubLimiter(clears: false);
        var deadHosts = new DeadHostTracker(threshold: 1);
        var factory = new ProviderRouterFactory(deadHosts, options: new LyntaiOptions(), limiter: limiter);

        var router = factory.For<VectorRequest, VectorResponse>(all, VectorResponse.Failure,
            c => c.Supports(ProviderKinds.Vector, ProviderOperation.Complete, accepts: ProviderKinds.Text));
        var reply = await router.CallAsync(new VectorRequest(["a"], Consumer: "memory"));

        Assert.Equal(ProviderVerdict.RateLimited, reply.Verdict); // a CLIENT-side refusal, like the text door
        Assert.Equal("memory", limiter.LastConsumer);
        Assert.Equal(0, good.Calls);
        Assert.False(deadHosts.IsDead("vector::good")); // no host was at fault, so none is benched
    }

    [Fact]
    public async Task An_UNTAGGED_application_kind_is_not_governed_even_with_caps_set()
    {
        // opting in is implementing IConsumerTagged on the request (and Usage on the response) — a kind
        // that carries no tag has said nothing about who is spending, so the wallet cannot bill it
        var tracker = new Lyntai.Inference.Budgeting.InMemoryUsageTracker();
        await tracker.RecordAsync("x", new ProviderUsage(CostUsd: 99.0));
        var provider = new AppProvider();
        var limiter = new StubLimiter(clears: false);
        var factory = new ProviderRouterFactory(new DeadHostTracker(),
            options: CappedOptions(costCap: 1.0), tracker: tracker, limiter: limiter);

        var router = factory.For<AppRequest, AppResponse>([provider],
            (v, d) => new AppResponse(v, d));
        var reply = await router.CallAsync(new AppRequest("go"));

        Assert.Equal(ProviderVerdict.Ok, reply.Verdict);
        Assert.Equal(1, provider.Calls);
        Assert.Equal(0, limiter.Asked); // not asked and overruled: never asked
    }

    [Fact]
    public void A_null_synthesize_is_refused_at_the_factory_rather_than_inside_the_router()
    {
        var factory = new ProviderRouterFactory(new DeadHostTracker());
        Assert.Throws<ArgumentNullException>(
            () => factory.For<VectorRequest, VectorResponse>([], null!));
    }
}
